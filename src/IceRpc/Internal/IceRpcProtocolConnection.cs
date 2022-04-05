// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    internal sealed class IceRpcProtocolConnection : IProtocolConnection
    {
        /// <inheritdoc/>
        public bool HasDispatchesInProgress
        {
            get
            {
                lock (_mutex)
                {
                    return _dispatches.Count > 0;
                }
            }
        }

        /// <inheritdoc/>
        public bool HasInvocationsInProgress
        {
            get
            {
                lock (_mutex)
                {
                    return _invocations.Count > 0;
                }
            }
        }

        /// <inheritdoc/>
        public ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>> PeerFields { get; private set; } =
            ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>>.Empty;

        /// <inheritdoc/>
        public event Action<string>? PeerShutdownInitiated;

        private IMultiplexedStream? _controlStream;
        private readonly HashSet<CancellationTokenSource> _dispatches = new();
        private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<IMultiplexedStream> _invocations = new();
        private bool _isShutdownCanceled;
        private bool _isShuttingDown;
        private long _lastRemoteBidirectionalStreamId = -1;
        // TODO: to we really need to keep track of this since we don't keep track of one-way requests?
        private long _lastRemoteUnidirectionalStreamId = -1;
        private readonly IDictionary<ConnectionFieldKey, OutgoingFieldValue> _localFields;
        private readonly object _mutex = new();
        private readonly IMultiplexedNetworkConnection _networkConnection;
        private IMultiplexedStream? _remoteControlStream;
        private readonly CancellationTokenSource _shutdownCancellationSource = new();
        private readonly TaskCompletionSource _waitForGoAwayCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task AcceptRequestsAsync(Connection connection, IDispatcher dispatcher)
        {
            while (true)
            {
                // Accepts a new stream.
                IMultiplexedStream stream = await _networkConnection.AcceptStreamAsync(default).ConfigureAwait(false);

                IceRpcRequestHeader header;
                IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields;
                FeatureCollection features = FeatureCollection.Empty;
                PipeReader reader = stream.Input;
                try
                {
                    ReadResult readResult = await reader.ReadSegmentAsync(
                        SliceEncoding.Slice20,
                        CancellationToken.None).ConfigureAwait(false);

                    if (readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException("received icerpc request with empty header");
                    }

                    (header, fields) = DecodeHeader(readResult.Buffer);
                    reader.AdvanceTo(readResult.Buffer.End);

                    // Decode Context from Fields and set corresponding feature.
                    if (fields.DecodeValue(
                        RequestFieldKey.Context,
                        (ref SliceDecoder decoder) => decoder.DecodeDictionary(
                            minKeySize: 1,
                            minValueSize: 1,
                            size => new Dictionary<string, string>(size),
                            keyDecodeFunc: (ref SliceDecoder decoder) => decoder.DecodeString(),
                            valueDecodeFunc: (ref SliceDecoder decoder) => decoder.DecodeString()))
                                is Dictionary<string, string> context && context.Count > 0)
                    {
                        features = features.WithContext(context);
                    }
                }
                catch (Exception ex)
                {
                    await stream.Input.CompleteAsync(ex).ConfigureAwait(false);
                    if (stream.IsBidirectional)
                    {
                        await stream.Output.CompleteAsync(ex).ConfigureAwait(false);
                    }
                    throw;
                }

                var request = new IncomingRequest(Protocol.IceRpc)
                {
                    Connection = connection,
                    Features = features,
                    Fields = fields,
                    IsOneway = !stream.IsBidirectional,
                    Operation = header.Operation,
                    Path = header.Path,
                    Payload = reader,
                };

                CancellationTokenSource? cancelDispatchSource = null;
                bool shuttingDown = false;
                lock (_mutex)
                {
                    // If shutting down, ignore the incoming request.
                    if (_isShuttingDown)
                    {
                        shuttingDown = true;
                    }
                    else
                    {
                        cancelDispatchSource = new();
                        _dispatches.Add(cancelDispatchSource);
                        if (stream.IsBidirectional)
                        {
                            _lastRemoteBidirectionalStreamId = stream.Id;
                        }
                        else
                        {
                            _lastRemoteUnidirectionalStreamId = stream.Id;
                        }

                        stream.OnShutdown(() =>
                        {
                            // TODO: review stream shutdown (see #930)

                            try
                            {
                                cancelDispatchSource.Cancel();
                            }
                            catch (ObjectDisposedException)
                            {
                                // TODO: we shouldn't have to catch this exception here (related to #930).
                            }

                            lock (_mutex)
                            {
                                // TODO: we need to decouple the completion of a dispatch from its cancellation token
                                // source. A dispatch ends once the stream param receive or send terminates. We should
                                // probably keep the stream + cancelDispatchSource in _dispatches. To be able to cancel
                                // pending reads on request.Payload, pending reads on response.PayloadStream and pending
                                // flushes on output / payload writer.
                                _dispatches.Remove(cancelDispatchSource);

                                // If no more invocations or dispatches and shutting down, shutdown can complete.
                                if (_isShuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
                                {
                                    _dispatchesAndInvocationsCompleted.SetResult();
                                }
                            }
                        });
                    }
                }

                if (shuttingDown)
                {
                    // If shutting down, ignore the incoming request.
                    // TODO: replace with payload exception and error code
                    Exception exception = IceRpcStreamError.ConnectionShutdownByPeer.ToException();
                    await request.Payload.CompleteAsync(exception).ConfigureAwait(false);
                    await stream.Output.CompleteAsync(exception).ConfigureAwait(false);
                }
                else
                {
                    Debug.Assert(cancelDispatchSource != null);
                    _ = Task.Run(() => DispatchRequestAsync(request, stream, cancelDispatchSource));
                }
            }

            async Task DispatchRequestAsync(
                IncomingRequest request,
                IMultiplexedStream stream,
                CancellationTokenSource cancelDispatchSource)
            {
                using CancellationTokenSource? _ = cancelDispatchSource;

                OutgoingResponse response;
                try
                {
                    // The dispatcher is responsible for completing the incoming request payload and payload stream.
                    response = await dispatcher.DispatchAsync(
                        request,
                        cancelDispatchSource.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // If we catch an exception, we return a failure response with a Slice-encoded payload.

                    if (exception is OperationCanceledException)
                    {
                        // The dispatcher completes the incoming request payload even on failures. We just complete the
                        // stream output here.
                        await stream.Output.CompleteAsync(
                            IceRpcStreamError.DispatchCanceled.ToException()).ConfigureAwait(false);

                        // We're done since the completion of the stream output aborts the stream, we don't send a
                        // response.
                        return;
                    }

                    if (exception is not RemoteException remoteException || remoteException.ConvertToUnhandled)
                    {
                        remoteException = new DispatchException(
                            message: null,
                            exception is InvalidDataException ?
                                DispatchErrorCode.InvalidData : DispatchErrorCode.UnhandledException,
                            exception);
                    }

                    response = new OutgoingResponse(request)
                    {
                        Payload = SliceEncoding.Slice20.CreatePayloadFromRemoteException(remoteException),
                        ResultType = ResultType.Failure
                    };

                    if (remoteException.RetryPolicy != RetryPolicy.NoRetry)
                    {
                        RetryPolicy retryPolicy = remoteException.RetryPolicy;
                        response.Fields = response.Fields.With(
                            ResponseFieldKey.RetryPolicy,
                            (ref SliceEncoder encoder) => retryPolicy.Encode(ref encoder));
                    }
                }

                if (request.IsOneway)
                {
                    await response.CompleteAsync().ConfigureAwait(false);
                    return;
                }
                try
                {
                    EncodeHeader();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                // SendPayloadAsync takes care of the completion of the payload, payload stream and stream output.
                await SendPayloadAsync(response, stream.Output, CancellationToken.None).ConfigureAwait(false);

                void EncodeHeader()
                {
                    var encoder = new SliceEncoder(stream.Output, SliceEncoding.Slice20);

                    // Write the IceRpc response header.
                    Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(2);
                    int headerStartPos = encoder.EncodedByteCount;

                    new IceRpcResponseHeader(response.ResultType).Encode(ref encoder);

                    encoder.EncodeDictionary(
                        response.Fields,
                        (ref SliceEncoder encoder, ResponseFieldKey key) => encoder.EncodeResponseFieldKey(key),
                        (ref SliceEncoder encoder, OutgoingFieldValue value) => value.Encode(ref encoder));

                    // We're done with the header encoding, write the header size.
                    SliceEncoder.EncodeVarULong((ulong)(encoder.EncodedByteCount - headerStartPos), sizePlaceholder);
                }
            }

            static (IceRpcRequestHeader, IDictionary<RequestFieldKey, ReadOnlySequence<byte>>) DecodeHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, SliceEncoding.Slice20);
                var header = new IceRpcRequestHeader(ref decoder);
                IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields = decoder.DecodeFieldDictionary(
                    (ref SliceDecoder decoder) => decoder.DecodeRequestFieldKey());

                decoder.CheckEndOfBuffer(skipTaggedParams: false);
                return (header, fields);
            }
        }

        public void Dispose() => _shutdownCancellationSource.Dispose();

        public Task PingAsync(CancellationToken cancel) =>
            SendControlFrameAsync(IceRpcControlFrameType.Ping, encodeAction: null, cancel).AsTask();

        public async Task<IncomingResponse> SendRequestAsync(
            Connection connection,
            OutgoingRequest request,
            CancellationToken cancel)
        {
            PipeReader? responseReader = null;
            try
            {
                if (request.Proxy.Fragment.Length > 0)
                {
                    throw new NotSupportedException("the icerpc protocol does not support fragments");
                }

                // Create the stream.
                IMultiplexedStream stream = _networkConnection.CreateStream(bidirectional: !request.IsOneway);

                if (stream.IsBidirectional)
                {
                    responseReader = stream.Input;
                }

                // Keep track of the invocation for the shutdown logic.
                if (!request.IsOneway || request.PayloadStream != null)
                {
                    lock (_mutex)
                    {
                        if (_isShuttingDown)
                        {
                            throw new ConnectionClosedException("connection shutdown");
                        }
                        else
                        {
                            _invocations.Add(stream);

                            stream.OnShutdown(() =>
                            {
                                // TODO: review stream shutdown (see #930)

                                lock (_mutex)
                                {
                                    _invocations.Remove(stream);

                                    // If no more invocations or dispatches and shutting down, shutdown can complete.
                                    if (_isShuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
                                    {
                                        _dispatchesAndInvocationsCompleted.SetResult();
                                    }
                                }
                            });
                        }
                    }
                }

                EncodeHeader(stream.Output);

                // SendPayloadAsync takes care of the completion of the payloads and stream output.
                await SendPayloadAsync(request, stream.Output, cancel).ConfigureAwait(false);
                request.IsSent = true;
            }
            catch (Exception exception)
            {
                await request.CompleteAsync(exception).ConfigureAwait(false);
                if (responseReader != null)
                {
                    await responseReader.CompleteAsync(exception).ConfigureAwait(false);
                }
                throw;
            }

            if (request.IsOneway)
            {
                return new IncomingResponse(request);
            }

            Debug.Assert(responseReader != null);
            try
            {
                ReadResult readResult = await responseReader.ReadSegmentAsync(
                    SliceEncoding.Slice20,
                    cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    lock (_mutex)
                    {
                        if (_isShutdownCanceled)
                        {
                            // If shutdown is canceled, pending invocations are canceled.
                            throw new OperationCanceledException("shutdown canceled");
                        }
                        else if (_isShuttingDown)
                        {
                            // If shutdown isn't canceled, the cancellation indicates that the peer didn't dispatch the
                            // invocation.
                            throw new ConnectionClosedException("connection shutdown by peer");
                        }
                        else
                        {
                            // The stream was aborted (occurs if the Slic connection is disposed).
                            throw new ConnectionLostException();
                        }
                    }
                }

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException($"received icerpc response with empty header");
                }

                (IceRpcResponseHeader header, IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields) =
                    DecodeHeader(readResult.Buffer);
                responseReader.AdvanceTo(readResult.Buffer.End);

                RetryPolicy? retryPolicy = fields.DecodeValue(
                    ResponseFieldKey.RetryPolicy,
                    (ref SliceDecoder decoder) => new RetryPolicy(ref decoder));
                if (retryPolicy != null)
                {
                    request.Features = request.Features.With(retryPolicy);
                }

                return new IncomingResponse(request)
                {
                    Connection = connection,
                    Fields = fields,
                    Payload = responseReader,
                    ResultType = header.ResultType
                };
            }
            catch (MultiplexedStreamAbortedException exception)
            {
                await responseReader.CompleteAsync(exception).ConfigureAwait(false);
                if (exception.ErrorKind == MultiplexedStreamErrorKind.Protocol)
                {
                    throw exception.ToIceRpcException();
                }
                else
                {
                    throw new ConnectionLostException(exception);
                }
            }
            catch (OperationCanceledException)
            {
                // Notify the peer that we give up on receiving the response. The peer will cancel the dispatch upon
                // receiving this notification.
                await responseReader.CompleteAsync(
                    IceRpcStreamError.InvocationCanceled.ToException()).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                await responseReader.CompleteAsync(exception).ConfigureAwait(false);
                throw;
            }

            void EncodeHeader(PipeWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice20);

                // Write the IceRpc request header.
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(2);
                int headerStartPos = encoder.EncodedByteCount; // does not include the size

                var header = new IceRpcRequestHeader(request.Proxy.Path, request.Operation);

                header.Encode(ref encoder);

                // We cannot use request.Features.GetContext here because it doesn't distinguish between empty and not
                // set context.
                if (request.Features.Get<Context>()?.Value is IDictionary<string, string> context)
                {
                    if (context.Count == 0)
                    {
                        // make sure it's not set.
                        request.Fields = request.Fields.Without(RequestFieldKey.Context);
                    }
                    else
                    {
                        request.Fields = request.Fields.With(
                            RequestFieldKey.Context,
                            (ref SliceEncoder encoder) => encoder.EncodeDictionary(
                                context,
                                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value),
                                (ref SliceEncoder encoder, string value) => encoder.EncodeString(value)));
                    }
                }

                encoder.EncodeDictionary(
                    request.Fields,
                    (ref SliceEncoder encoder, RequestFieldKey key) => encoder.EncodeRequestFieldKey(key),
                    (ref SliceEncoder encoder, OutgoingFieldValue value) => value.Encode(ref encoder));

                // We're done with the header encoding, write the header size.
                SliceEncoder.EncodeVarULong((ulong)(encoder.EncodedByteCount - headerStartPos), sizePlaceholder);
            }

            static (IceRpcResponseHeader, IDictionary<ResponseFieldKey, ReadOnlySequence<byte>>) DecodeHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, SliceEncoding.Slice20);
                var header = new IceRpcResponseHeader(ref decoder);
                IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields =
                    decoder.DecodeFieldDictionary((ref SliceDecoder decoder) => decoder.DecodeResponseFieldKey());

                decoder.CheckEndOfBuffer(skipTaggedParams: false);
                return (header, fields);
            }
        }

        /// <inheritdoc/>
        public async Task ShutdownAsync(string message, CancellationToken cancel)
        {
            IceRpcGoAway goAwayFrame;
            lock (_mutex)
            {
                // Mark the connection as shutting down to prevent further requests from being accepted.
                _isShuttingDown = true;
                if (_invocations.Count == 0 && _dispatches.Count == 0)
                {
                    _dispatchesAndInvocationsCompleted.SetResult();
                }
                goAwayFrame = new(_lastRemoteBidirectionalStreamId, _lastRemoteUnidirectionalStreamId, message);
            }

            // Canceling Shutdown will cancel dispatches and invocations to speed up shutdown.
            using CancellationTokenRegistration _ = cancel.Register(() =>
                {
                    try
                    {
                        _shutdownCancellationSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                });

            // Send GoAway frame.
            await SendControlFrameAsync(
                IceRpcControlFrameType.GoAway,
                (ref SliceEncoder encoder) => goAwayFrame.Encode(ref encoder),
                CancellationToken.None).ConfigureAwait(false);

            // Ensure WaitForGoAway completes before continuing.
            await _waitForGoAwayCompleted.Task.ConfigureAwait(false);

            try
            {
                // Wait for dispatches and invocations to complete.
                await _dispatchesAndInvocationsCompleted.Task.WaitAsync(
                    _shutdownCancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancel invocations and dispatches to speed up the shutdown.
                IEnumerable<IMultiplexedStream> invocations;
                IEnumerable<CancellationTokenSource> dispatches;
                lock (_mutex)
                {
                    _isShutdownCanceled = true;
                    invocations = _invocations.ToArray();
                    dispatches = _dispatches.ToArray();
                }

                foreach (CancellationTokenSource dispatchCancelSource in dispatches)
                {
                    try
                    {
                        dispatchCancelSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore, the dispatch completed concurrently.
                    }
                }

                foreach (IMultiplexedStream stream in invocations)
                {
                    stream.Input.CancelPendingRead();
                }

                // Wait again for dispatches and invocations to complete.
                await _dispatchesAndInvocationsCompleted.Task.ConfigureAwait(false);
            }

            // We are done with the shutdown, notify the peer that shutdown completed on our side.
            await SendControlFrameAsync(
                IceRpcControlFrameType.GoAwayCompleted,
                encodeAction: null,
                CancellationToken.None).ConfigureAwait(false);

            // Wait for the peer to complete its side of the shutdown.
            await ReceiveControlFrameHeaderAsync(
                IceRpcControlFrameType.GoAwayCompleted,
                CancellationToken.None).ConfigureAwait(false);

            // GoAwayCompleted has no body
        }

        /// <inheritdoc/>
        internal IceRpcProtocolConnection(
            IMultiplexedNetworkConnection networkConnection,
            IDictionary<ConnectionFieldKey, OutgoingFieldValue> localFields)
        {
            _networkConnection = networkConnection;
            _localFields = localFields;
        }

        internal async Task InitializeAsync(CancellationToken cancel)
        {
            // Create the control stream and send the protocol initialize frame
            _controlStream = _networkConnection.CreateStream(false);

            await SendControlFrameAsync(
                IceRpcControlFrameType.Initialize,
                (ref SliceEncoder encoder) =>
                    encoder.EncodeDictionary(
                        _localFields,
                        (ref SliceEncoder encoder, ConnectionFieldKey key) => encoder.EncodeConnectionFieldKey(key),
                        (ref SliceEncoder encoder, OutgoingFieldValue value) => value.Encode(ref encoder)),
                cancel).ConfigureAwait(false);

            // Wait for the remote control stream to be accepted and read the protocol initialize frame
            _remoteControlStream = await _networkConnection.AcceptStreamAsync(cancel).ConfigureAwait(false);

            await ReceiveControlFrameHeaderAsync(IceRpcControlFrameType.Initialize, cancel).ConfigureAwait(false);

            PeerFields = await ReceiveControlFrameBodyAsync(
                (ref SliceDecoder decoder) => decoder.DecodeFieldDictionary(
                    (ref SliceDecoder decoder) => decoder.DecodeConnectionFieldKey()).ToImmutableDictionary(),
                cancel).ConfigureAwait(false);

            // Start a task to wait to receive the go away frame to initiate shutdown.
            _ = Task.Run(() => WaitForGoAwayAsync(), CancellationToken.None);
        }

        private async ValueTask<T> ReceiveControlFrameBodyAsync<T>(
            DecodeFunc<T> decodeFunc,
            CancellationToken cancel)
        {
            PipeReader input = _remoteControlStream!.Input;
            ReadResult readResult = await input.ReadSegmentAsync(SliceEncoding.Slice20, cancel).ConfigureAwait(false);
            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            try
            {
                return SliceEncoding.Slice20.DecodeBuffer(readResult.Buffer, decodeFunc);
            }
            finally
            {
                if (!readResult.Buffer.IsEmpty)
                {
                    input.AdvanceTo(readResult.Buffer.End);
                }
            }
        }

        private async ValueTask ReceiveControlFrameHeaderAsync(
            IceRpcControlFrameType expectedFrameType,
            CancellationToken cancel)
        {
            Debug.Assert(expectedFrameType != IceRpcControlFrameType.Ping);
            PipeReader input = _remoteControlStream!.Input;

            while (true)
            {
                ReadResult readResult = await input.ReadAsync(cancel).ConfigureAwait(false);
                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }
                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException("invalid empty control frame");
                }

                IceRpcControlFrameType frameType = readResult.Buffer.FirstSpan[0].AsIceRpcControlFrameType();
                input.AdvanceTo(readResult.Buffer.GetPosition(1));

                if (frameType == IceRpcControlFrameType.Ping)
                {
                    continue;
                }

                if (frameType != expectedFrameType)
                {
                    throw new InvalidDataException(
                       $"received frame type {frameType} but expected {expectedFrameType}");
                }
                else
                {
                    // Received expected frame type, returning.
                    break; // while
                }
            }
        }

        private ValueTask<FlushResult> SendControlFrameAsync(
            IceRpcControlFrameType frameType,
            EncodeAction? encodeAction,
            CancellationToken cancel)
        {
            PipeWriter output = _controlStream!.Output;
            output.GetSpan()[0] = (byte)frameType;
            output.Advance(1);

            if (encodeAction != null)
            {
                var encoder = new SliceEncoder(output, SliceEncoding.Slice20);
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(2); // TODO: switch to MaxHeaderSize
                int startPos = encoder.EncodedByteCount; // does not include the size
                encodeAction?.Invoke(ref encoder);
                SliceEncoder.EncodeVarULong((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
            }

            return frameType == IceRpcControlFrameType.GoAwayCompleted ?
                output.WriteAsync(ReadOnlySequence<byte>.Empty, endStream: true, cancel) :
                output.FlushAsync(cancel);
        }

        /// <summary>Sends the payload and payload stream of an outgoing frame. SendPayloadAsync completes the payload
        /// if successful. It completes the output only if there's no payload stream. Otherwise, it starts a streaming
        /// task that is responsible for completing the payload stream and the output.</summary>
        private static async ValueTask SendPayloadAsync(
            OutgoingFrame outgoingFrame,
            PipeWriter output,
            CancellationToken cancel)
        {
            PipeWriter payloadWriter = outgoingFrame.GetPayloadWriter(output);

            try
            {
                await CopyReaderToWriterAsync(
                    outgoingFrame.Payload,
                    payloadWriter,
                    endStream: outgoingFrame.PayloadStream == null,
                    cancel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await payloadWriter.CompleteAsync(exception).ConfigureAwait(false);
                throw;
            }

            await outgoingFrame.Payload.CompleteAsync().ConfigureAwait(false);

            if (outgoingFrame.PayloadStream == null)
            {
                await payloadWriter.CompleteAsync().ConfigureAwait(false);
            }
            else
            {
                // Send payloadStream in the background.
                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            await CopyReaderToWriterAsync(
                                outgoingFrame.PayloadStream,
                                payloadWriter,
                                endStream: true,
                                CancellationToken.None).ConfigureAwait(false);

                            await outgoingFrame.PayloadStream.CompleteAsync().ConfigureAwait(false);
                            await payloadWriter.CompleteAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            await outgoingFrame.PayloadStream.CompleteAsync(exception).ConfigureAwait(false);
                            await payloadWriter.CompleteAsync(exception).ConfigureAwait(false);
                        }
                    },
                    cancel);
            }

            static async Task CopyReaderToWriterAsync(
                PipeReader reader,
                PipeWriter writer,
                bool endStream,
                CancellationToken cancel)
            {
                FlushResult flushResult;
                ReadResult readResult;
                do
                {
                    readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        flushResult = await writer.WriteAsync(
                            readResult.Buffer,
                            readResult.IsCompleted && endStream,
                            cancel).ConfigureAwait(false);
                    }
                    finally
                    {
                        reader.AdvanceTo(readResult.Buffer.End);
                    }
                } while (!readResult.IsCompleted && !readResult.IsCanceled &&
                         !flushResult.IsCompleted && !flushResult.IsCanceled);

                // An application payload writer decorator can return a canceled flush result.
                // TODO: is this really possible?
                // See https://github.com/zeroc-ice/icerpc-csharp/pull/977#discussion_r837440210
                if (flushResult.IsCanceled)
                {
                    throw new OperationCanceledException("payload writer canceled");
                }

                // The remote reader gracefully complete the stream input pipe. TODO: which exception should we throw
                // here? We throw OperationCanceledException... which implies that if the frame is an outgoing request
                // is won't be retried.
                if (flushResult.IsCompleted)
                {
                    throw new OperationCanceledException("peer stopped reading the payload");
                }
            }
        }

        private async Task WaitForGoAwayAsync()
        {
            try
            {
                // Receive and decode GoAway frame

                await ReceiveControlFrameHeaderAsync(
                    IceRpcControlFrameType.GoAway,
                    CancellationToken.None).ConfigureAwait(false);

                IceRpcGoAway goAwayFrame = await ReceiveControlFrameBodyAsync(
                    (ref SliceDecoder decoder) => new IceRpcGoAway(ref decoder),
                    CancellationToken.None).ConfigureAwait(false);

                // Raise the peer shutdown initiated event.
                try
                {
                    PeerShutdownInitiated?.Invoke(goAwayFrame.Message);
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"{nameof(PeerShutdownInitiated)} raised unexpected exception\n{ex}");
                }

                IEnumerable<IMultiplexedStream> invocations;
                lock (_mutex)
                {
                    // Mark the connection as shutting down to prevent further requests from being accepted.
                    _isShuttingDown = true;

                    // Cancel the invocations that were not dispatched by the peer.
                    invocations = _invocations.Where(stream =>
                        !stream.IsStarted ||
                        (stream.Id > (stream.IsBidirectional ?
                            goAwayFrame.LastBidirectionalStreamId :
                            goAwayFrame.LastUnidirectionalStreamId))).ToArray();
                }

                foreach (IMultiplexedStream stream in invocations)
                {
                    stream.Input.CancelPendingRead();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected if shutdown is initiated on the connection.
                throw;
            }
            finally
            {
                _waitForGoAwayCompleted.SetResult();
            }
        }
    }
}
