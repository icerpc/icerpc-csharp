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
        private readonly HashSet<IncomingRequest> _dispatches = new();
        private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly HashSet<IMultiplexedStream> _invocations = new();
        private long _lastRemoteBidirectionalStreamId = -1;
        // TODO: to we really need to keep track of this since we don't keep track of one-way requests?
        private long _lastRemoteUnidirectionalStreamId = -1;
        private readonly IDictionary<ConnectionFieldKey, OutgoingFieldValue> _localFields;

        private readonly object _mutex = new();
        private readonly IMultiplexedNetworkConnection _networkConnection;
        private IMultiplexedStream? _remoteControlStream;
        private readonly CancellationTokenSource _shutdownCancellationSource = new();
        private bool _shutdownCanceled;
        private bool _shuttingDown;
        private readonly TaskCompletionSource _waitForGoAwayCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => _shutdownCancellationSource.Dispose();

        public Task PingAsync(CancellationToken cancel) =>
            SendControlFrameAsync(IceRpcControlFrameType.Ping, encodeAction: null, cancel).AsTask();

        /// <inheritdoc/>
        public async Task<IncomingRequest> ReceiveRequestAsync()
        {
            while (true)
            {
                // Accepts a new stream.
                IMultiplexedStream stream;
                try
                {
                    stream = await _networkConnection.AcceptStreamAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    lock (_mutex)
                    {
                        if (_shuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
                        {
                            // The connection was gracefully shut down, raise ConnectionClosedException here to ensure
                            // that the ClosedEvent will report this exception instead of the transport failure.
                            throw new ConnectionClosedException("connection gracefully shut down");
                        }
                    }
                    throw;
                }

                IceRpcRequestHeader header;
                IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields;
                FeatureCollection features = FeatureCollection.Empty;
                PipeReader reader = stream.Input;
                try
                {
                    ReadResult readResult = await reader.ReadSegmentAsync(CancellationToken.None).ConfigureAwait(false);

                    if (readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException($"received icerpc request with empty header");
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
                    Features = features,
                    Fields = fields,
                    IsOneway = !stream.IsBidirectional,
                    Operation = header.Operation,
                    Path = header.Path,
                    Payload = reader,
                    PayloadEncoding = header.PayloadEncoding.Length > 0 ?
                        Encoding.FromString(header.PayloadEncoding) : IceRpcDefinitions.Encoding,
                    ResponseWriter = stream.IsBidirectional ? stream.Output : InvalidPipeWriter.Instance
                };

                lock (_mutex)
                {
                    // If shutting down, ignore the incoming request and continue receiving frames until the connection
                    // is closed.
                    if (!_shuttingDown)
                    {
                        _dispatches.Add(request);
                        request.CancelDispatchSource = new();

                        if (stream.IsBidirectional)
                        {
                            _lastRemoteBidirectionalStreamId = stream.Id;
                        }
                        else
                        {
                            _lastRemoteUnidirectionalStreamId = stream.Id;
                        }

                        stream.ShutdownAction = () =>
                        {
                            request.CancelDispatchSource.Cancel();
                            request.CancelDispatchSource.Dispose();

                            lock (_mutex)
                            {
                                _dispatches.Remove(request);

                                // If no more invocations or dispatches and shutting down, shutdown can complete.
                                if (_shuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
                                {
                                    _dispatchesAndInvocationsCompleted.SetResult();
                                }
                            }
                        };
                        return request;
                    }
                }

                static (IceRpcRequestHeader, IDictionary<RequestFieldKey, ReadOnlySequence<byte>>) DecodeHeader(
                    ReadOnlySequence<byte> buffer)
                {
                    var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                    var header =
                        (new IceRpcRequestHeader(ref decoder),
                        decoder.DecodeFieldDictionary((ref SliceDecoder decoder) => decoder.DecodeRequestFieldKey()));

                    decoder.CheckEndOfBuffer(skipTaggedParams: false);
                    return header;
                }
            }
        }

        /// <inheritdoc/>
        public async Task<IncomingResponse> ReceiveResponseAsync(OutgoingRequest request, CancellationToken cancel)
        {
            // This class sent this request and set ResponseReader on it.
            Debug.Assert(request.ResponseReader != null);
            Debug.Assert(!request.IsOneway);

            IceRpcResponseHeader header;
            IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields;

            PipeReader responseReader = request.ResponseReader;

            try
            {
                ReadResult readResult = await responseReader.ReadSegmentAsync(cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    lock (_mutex)
                    {
                        if (_shutdownCanceled)
                        {
                            // If shutdown is canceled, pending invocations are canceled.
                            throw new OperationCanceledException("shutdown canceled");
                        }
                        else if (_shuttingDown)
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

                (header, fields) = DecodeHeader(readResult.Buffer);
                responseReader.AdvanceTo(readResult.Buffer.End);

                RetryPolicy? retryPolicy = fields.DecodeValue(
                    ResponseFieldKey.RetryPolicy, (ref SliceDecoder decoder) => new RetryPolicy(ref decoder));
                if (retryPolicy != null)
                {
                    request.Features = request.Features.With(retryPolicy);
                }
            }
            catch (MultiplexedStreamAbortedException ex)
            {
                await responseReader.CompleteAsync(ex).ConfigureAwait(false);
                if (ex.ErrorKind == MultiplexedStreamErrorKind.Protocol)
                {
                    throw ex.ToIceRpcException();
                }
                else
                {
                    throw new ConnectionLostException(ex);
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
            catch (Exception ex)
            {
                await responseReader.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            return new IncomingResponse(request)
            {
                Fields = fields,
                Payload = responseReader,
                ResultType = header.ResultType
            };

            static (IceRpcResponseHeader, IDictionary<ResponseFieldKey, ReadOnlySequence<byte>>) DecodeHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                var header =
                    (new IceRpcResponseHeader(ref decoder),
                    decoder.DecodeFieldDictionary((ref SliceDecoder decoder) => decoder.DecodeResponseFieldKey()));

                decoder.CheckEndOfBuffer(skipTaggedParams: false);
                return header;
            }
        }

        /// <inheritdoc/>
        public async Task SendRequestAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.Proxy.Fragment.Length > 0)
            {
                throw new NotSupportedException("the icerpc protocol does not support fragments");
            }

            IMultiplexedStream? stream = null;
            try
            {
                // Create the stream.
                stream = _networkConnection.CreateStream(!request.IsOneway);

                // Keep track of the invocation for the shutdown logic.
                if (!request.IsOneway || request.PayloadSourceStream != null)
                {
                    bool shutdown = false;
                    lock (_mutex)
                    {
                        if (_shuttingDown)
                        {
                            shutdown = true;
                        }
                        else
                        {
                            _invocations.Add(stream);

                            stream.ShutdownAction = () =>
                            {
                                lock (_mutex)
                                {
                                    _invocations.Remove(stream);

                                    // If no more invocations or dispatches and shutting down, shutdown can complete.
                                    if (_shuttingDown && _invocations.Count == 0 && _dispatches.Count == 0)
                                    {
                                        _dispatchesAndInvocationsCompleted.SetResult();
                                    }
                                }
                            };
                        }
                    }

                    if (shutdown)
                    {
                        throw new ConnectionClosedException("connection shutdown");
                    }
                }

                // If the application sets the payload sink, the initial payload sink is set and we need to set the
                // stream output on the delayed pipe writer decorator. Otherwise, we directly use the stream output.
                PipeWriter payloadSink;
                if (request.InitialPayloadSink == null)
                {
                    payloadSink = stream.Output;
                }
                else
                {
                    request.InitialPayloadSink.SetDecoratee(stream.Output);
                    payloadSink = request.PayloadSink;
                }

                EncodeHeader(stream.Output);
                await SendPayloadAsync(request, payloadSink, cancel).ConfigureAwait(false);

                request.IsSent = true;

                if (!request.IsOneway)
                {
                    request.ResponseReader = stream.Input;
                }
            }
            catch (Exception ex)
            {
                await request.CompleteAsync(ex).ConfigureAwait(false);

                if (stream != null)
                {
                    await stream.Output.CompleteAsync(ex).ConfigureAwait(false);
                    if (stream.IsBidirectional)
                    {
                        await stream.Input.CompleteAsync(ex).ConfigureAwait(false);
                    }
                }
                throw;
            }

            void EncodeHeader(PipeWriter writer)
            {
                var encoder = new SliceEncoder(writer, Encoding.Slice20);

                // Write the IceRpc request header.
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(2);
                int headerStartPos = encoder.EncodedByteCount; // does not include the size

                var header = new IceRpcRequestHeader(
                    request.Proxy.Path,
                    request.Operation,
                    request.PayloadEncoding == IceRpcDefinitions.Encoding ? "" : request.PayloadEncoding.ToString());

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
                Slice20Encoding.EncodeSize(encoder.EncodedByteCount - headerStartPos, sizePlaceholder.Span);
            }
        }

        /// <inheritdoc/>
        public async Task SendResponseAsync(
            OutgoingResponse response,
            IncomingRequest request,
            CancellationToken cancel)
        {
            if (request.IsOneway)
            {
                await response.CompleteAsync().ConfigureAwait(false);
                return;
            }

            try
            {
                EncodeHeader();
                await SendPayloadAsync(response, response.PayloadSink, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await response.CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }

            void EncodeHeader()
            {
                var encoder = new SliceEncoder(request.ResponseWriter, Encoding.Slice20);

                // Write the IceRpc response header.
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(2);
                int headerStartPos = encoder.EncodedByteCount;

                new IceRpcResponseHeader(response.ResultType).Encode(ref encoder);

                encoder.EncodeDictionary(
                    response.Fields,
                    (ref SliceEncoder encoder, ResponseFieldKey key) => encoder.EncodeResponseFieldKey(key),
                    (ref SliceEncoder encoder, OutgoingFieldValue value) => value.Encode(ref encoder));

                // We're done with the header encoding, write the header size.
                Slice20Encoding.EncodeSize(encoder.EncodedByteCount - headerStartPos, sizePlaceholder.Span);
            }
        }

        /// <inheritdoc/>
        public async Task ShutdownAsync(string message, CancellationToken cancel)
        {
            lock (_mutex)
            {
                // Mark the connection as shutting down to prevent further requests from being accepted.
                _shuttingDown = true;
                if (_invocations.Count == 0 && _dispatches.Count == 0)
                {
                    _dispatchesAndInvocationsCompleted.SetResult();
                }
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
                (ref SliceEncoder encoder) => new IceRpcGoAway(
                    _lastRemoteBidirectionalStreamId,
                    _lastRemoteUnidirectionalStreamId,
                    message).Encode(ref encoder),
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
                IEnumerable<IncomingRequest> dispatches;
                lock (_mutex)
                {
                    _shutdownCanceled = true;
                    invocations = _invocations.ToArray();
                    dispatches = _dispatches.ToArray();
                }

                foreach (IncomingRequest request in dispatches)
                {
                    try
                    {
                        request.CancelDispatchSource!.Cancel();
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
            ReadResult readResult = await input.ReadSegmentAsync(cancel).ConfigureAwait(false);
            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            try
            {
                return Encoding.Slice20.DecodeBuffer(readResult.Buffer, decodeFunc);
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
                var encoder = new SliceEncoder(output, Encoding.Slice20);
                Memory<byte> sizePlaceholder = encoder.GetPlaceholderMemory(2); // TODO: switch to MaxHeaderSize
                int startPos = encoder.EncodedByteCount; // does not include the size
                encodeAction?.Invoke(ref encoder);
                Slice20Encoding.EncodeSize(encoder.EncodedByteCount - startPos, sizePlaceholder.Span);
            }

            return frameType == IceRpcControlFrameType.GoAwayCompleted ?
                output.WriteAsync(ReadOnlySequence<byte>.Empty, completeWhenDone: true, cancel) :
                output.FlushAsync(cancel);
        }

        /// <summary>Sends the payload source and payload source stream of an outgoing frame.</summary>
        private static async ValueTask SendPayloadAsync(
            OutgoingFrame outgoingFrame,
            PipeWriter payloadSink,
            CancellationToken cancel)
        {
            FlushResult flushResult = await payloadSink.CopyFromAsync(
                outgoingFrame.PayloadSource,
                outgoingFrame.PayloadSourceStream == null,
                cancel).ConfigureAwait(false);

            Debug.Assert(!flushResult.IsCanceled); // not implemented yet

            if (flushResult.IsCompleted)
            {
                // The remote reader gracefully complete the stream input pipe. TODO: which exception should we
                // throw here? We throw OperationCanceledException... which implies that if the frame is an outgoing
                // request is won't be retried.
                throw new OperationCanceledException("peer stopped reading the payload");
            }

            if (outgoingFrame.PayloadSourceStream == null)
            {
                await outgoingFrame.CompleteAsync().ConfigureAwait(false);
            }
            else
            {
                // Just complete the payload source for now.
                await outgoingFrame.PayloadSource.CompleteAsync().ConfigureAwait(false);

                // Send payloadSourceStream in the background
                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            _ = await payloadSink.CopyFromAsync(
                                outgoingFrame.PayloadSourceStream,
                                completeWhenDone: true,
                                CancellationToken.None).ConfigureAwait(false);

                            await outgoingFrame.CompleteAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            await outgoingFrame.CompleteAsync(ex).ConfigureAwait(false);
                        }
                    },
                    cancel);
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
                    _shuttingDown = true;

                    // Cancel the invocations that were not dispatched by the peer.
                    invocations = _invocations.Where(stream =>
                        !stream.IsStarted ||
                        (stream.Id > (stream.IsBidirectional ?
                            goAwayFrame.LastBidirectionalStreamId :
                            goAwayFrame.LastUnidirectionalStreamId))).ToArray();
                }

                foreach (IMultiplexedStream stream in invocations)
                {
                    // Cancel the invocation pending read response.
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
