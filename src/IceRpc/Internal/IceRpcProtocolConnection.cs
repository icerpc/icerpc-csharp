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
                    return _cancelDispatchSources.Count > 0;
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
                    return _invocationCount > 0;
                }
            }
        }

        /// <inheritdoc/>
        public ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>> PeerFields { get; private set; } =
            ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>>.Empty;

        /// <inheritdoc/>
        public event Action<string>? PeerShutdownInitiated;

        private IMultiplexedStream? _controlStream;
        private readonly HashSet<CancellationTokenSource> _cancelDispatchSources = new();
        private readonly TaskCompletionSource _streamsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<IMultiplexedStream> _streams = new();
        private int _invocationCount;
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
                try
                {
                    ReadResult readResult = await stream.Input.ReadSegmentAsync(
                        SliceEncoding.Slice20,
                        CancellationToken.None).ConfigureAwait(false);

                    if (readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException("received icerpc request with empty header");
                    }

                    (header, fields) = DecodeHeader(readResult.Buffer);
                    stream.Input.AdvanceTo(readResult.Buffer.End);

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
                catch (Exception exception)
                {
                    await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
                    if (stream.IsBidirectional)
                    {
                        await stream.Output.CompleteAsync(exception).ConfigureAwait(false);
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
                    Payload = stream.Input,
                };

                CancellationTokenSource? cancelDispatchSource = null;
                bool isShuttingDown = false;
                lock (_mutex)
                {
                    // If shutting down, ignore the incoming request.
                    if (_isShuttingDown)
                    {
                        isShuttingDown = true;
                    }
                    else
                    {
                        cancelDispatchSource = new();
                        _cancelDispatchSources.Add(cancelDispatchSource);

                        if (stream.IsBidirectional)
                        {
                            _lastRemoteBidirectionalStreamId = stream.Id;
                        }
                        else
                        {
                            _lastRemoteUnidirectionalStreamId = stream.Id;
                        }

                        AddStream(stream);
                    }
                }

                if (isShuttingDown)
                {
                    // If shutting down, ignore the incoming request.
                    // TODO: replace with payload exception and error code
                    Exception exception = IceRpcStreamError.ConnectionShutdownByPeer.ToException();
                    await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
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
                // If the peer input pipe reader is completed while the request is being dispatched, we cancel the
                // dispatch. There's no point in continuing the dispatch if the peer is no longer interested in the
                // response.
                stream.OnPeerInputCompleted(() =>
                    {
                        try
                        {
                            cancelDispatchSource.Cancel();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    });

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
                finally
                {
                    lock (_mutex)
                    {
                        _cancelDispatchSources.Remove(cancelDispatchSource);
                        cancelDispatchSource.Dispose();
                    }
                }

                if (request.IsOneway)
                {
                    await response.CompleteAsync().ConfigureAwait(false);
                    return;
                }

                EncodeHeader();
                // SendPayloadAsync takes care of the completion of the payload, payload stream and stream output.
                await SendPayloadAsync(response, stream, CancellationToken.None).ConfigureAwait(false);

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
            IMultiplexedStream? stream = null;
            try
            {
                if (request.Proxy.Fragment.Length > 0)
                {
                    throw new NotSupportedException("the icerpc protocol does not support fragments");
                }

                // Create the stream.
                stream = _networkConnection.CreateStream(bidirectional: !request.IsOneway);

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
                            AddStream(stream);
                        }
                    }
                }

                EncodeHeader(stream.Output);

                // SendPayloadAsync takes care of the completion of the payloads and stream output.
                await SendPayloadAsync(request, stream, cancel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await request.CompleteAsync(exception).ConfigureAwait(false);
                if (stream != null)
                {
                    await stream.Output.CompleteAsync(exception).ConfigureAwait(false);
                    if (stream.IsBidirectional)
                    {
                        await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
                    }
                }
                throw;
            }

            request.IsSent = true;

            if (request.IsOneway)
            {
                return new IncomingResponse(request);
            }

            Debug.Assert(stream != null);
            try
            {
                ReadResult readResult = await stream.Input.ReadSegmentAsync(
                    SliceEncoding.Slice20,
                    cancel).ConfigureAwait(false);

                // Nothing cancels the stream input pipe reader.
                Debug.Assert(!readResult.IsCanceled);

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException($"received icerpc response with empty header");
                }

                (IceRpcResponseHeader header, IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields) =
                    DecodeHeader(readResult.Buffer);
                stream.Input.AdvanceTo(readResult.Buffer.End);

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
                    Payload = stream.Input,
                    ResultType = header.ResultType
                };
            }
            catch (MultiplexedStreamAbortedException exception)
            {
                await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
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
                await stream.Input.CompleteAsync(
                    IceRpcStreamError.InvocationCanceled.ToException()).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
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
                if (_streams.Count == 0)
                {
                    _streamsCompleted.SetResult();
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
                // Wait for streams to complete.
                await _streamsCompleted.Task.WaitAsync(_shutdownCancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                IEnumerable<IMultiplexedStream> streams;
                IEnumerable<CancellationTokenSource> cancelDispatchSources;
                lock (_mutex)
                {
                    streams = _streams.ToArray();
                    cancelDispatchSources = _cancelDispatchSources.ToArray();
                }

                // Cancel pending dispatches.
                foreach (CancellationTokenSource cancelDispatchSource in cancelDispatchSources)
                {
                    try
                    {
                        cancelDispatchSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore, the dispatch completed concurrently.
                    }
                }

                // Abort streams.
                MultiplexedStreamAbortedException exception = IceRpcStreamError.ConnectionShutdown.ToException();
                foreach (IMultiplexedStream stream in streams)
                {
                    stream.Abort(exception);
                }

                // Wait again for dispatches and invocations to complete.
                await _streamsCompleted.Task.ConfigureAwait(false);
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

        private void AddStream(IMultiplexedStream stream)
        {
            _streams.Add(stream);

            if (!stream.IsRemote)
            {
                ++_invocationCount;
            }

            stream.OnShutdown(() =>
                {
                    lock (_mutex)
                    {
                        if (!stream.IsRemote)
                        {
                            --_invocationCount;
                        }

                        _streams.Remove(stream);

                        // If no more streams and shutting down, we can set the _streamsCompleted task completion source
                        // as completed to allow shutdown to progress.
                        if (_isShuttingDown && _streams.Count == 0)
                        {
                            _streamsCompleted.SetResult();
                        }
                    }
                });
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
            IMultiplexedStream stream,
            CancellationToken cancel)
        {
            PipeWriter payloadWriter = outgoingFrame.GetPayloadWriter(stream.Output);

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
                // Send PayloadStream in the background.
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

            async Task CopyReaderToWriterAsync(
                PipeReader reader,
                PipeWriter writer,
                bool endStream,
                CancellationToken cancel)
            {
                // If the peer completes its input pipe reader, we cancel the pending read on the payload. If the
                // reading from the payload, the payload read will be interrupted.
                stream.OnPeerInputCompleted(reader.CancelPendingRead);

                while (true)
                {
                    ReadResult readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

                    FlushResult flushResult;
                    if (readResult.IsCanceled)
                    {
                        // If the peer input pipe reader was completed, this will throw with the reason of the pipe
                        // reader completion.
                        flushResult = await writer.FlushAsync(cancel).ConfigureAwait(false);
                    }
                    else
                    {
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
                    }

                    if (readResult.IsCompleted)
                    {
                        // We're done if there's no more data to send for the payload.
                        break;
                    }
                    else if (readResult.IsCanceled)
                    {
                        throw new OperationCanceledException(
                            "payload sending was canceled by a payload reader decorator");

                    }

                    if (flushResult.IsCompleted)
                    {
                        // The remote reader gracefully completed the stream input pipe.
                        throw new OperationCanceledException("peer stopped reading the payload");
                    }
                    else if (flushResult.IsCanceled)
                    {
                        throw new OperationCanceledException(
                            "payload sending was canceled by a payload writer interceptor");
                    }
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
                    invocations = _streams.Where(stream =>
                        !stream.IsRemote &&
                        (!stream.IsStarted || (stream.Id > (stream.IsBidirectional ?
                            goAwayFrame.LastBidirectionalStreamId :
                            goAwayFrame.LastUnidirectionalStreamId)))).ToArray();
                }

                // Abort streams for invocations that were not dispatched by the peer. The invocations will throw
                // ConnectionClosedException which is retryable.
                MultiplexedStreamAbortedException exception = IceRpcStreamError.ConnectionShutdownByPeer.ToException();
                foreach (IMultiplexedStream stream in invocations)
                {
                    stream.Abort(exception);
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
