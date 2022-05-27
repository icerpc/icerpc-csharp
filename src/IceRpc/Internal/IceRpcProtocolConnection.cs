// Copyright (c) ZeroC, Inc. All rights reserved.

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

        public TimeSpan LastActivity => _networkConnection.LastActivity;

        public Action<string>? PeerShutdownInitiated { get; set; }

        public Protocol Protocol => Protocol.IceRpc;

        private IMultiplexedStream? _controlStream;
        private readonly HashSet<CancellationTokenSource> _cancelDispatchSources = new();
        private readonly AsyncSemaphore _controlStreamSemaphore = new(1, 1);
        private readonly IDispatcher _dispatcher;

        // The number of bytes we need to encode a size up to _maxRemoteHeaderSize. It's 2 for DefaultMaxHeaderSize.
        private int _headerSizeLength = 2;
        private int _invocationCount;
        private bool _isDisposed;
        private bool _isShuttingDown;
        private long _lastRemoteBidirectionalStreamId = -1;
        // TODO: to we really need to keep track of this since we don't keep track of one-way requests?
        private long _lastRemoteUnidirectionalStreamId = -1;
        private readonly int _maxLocalHeaderSize;
        private int _maxRemoteHeaderSize = ConnectionOptions.DefaultMaxIceRpcHeaderSize;
        private readonly object _mutex = new();
        private readonly IMultiplexedNetworkConnection _networkConnection;
        private IMultiplexedStream? _remoteControlStream;

        private readonly HashSet<IMultiplexedStream> _streams = new();

        private readonly TaskCompletionSource _streamsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<IceRpcGoAway>? _waitForGoAwayFrame;

        public void Abort(Exception exception)
        {
            lock (_mutex)
            {
                if (_isDisposed)
                {
                    return;
                }
                _isDisposed = true;
            }

            _networkConnection.Abort(exception);

            _ = AbortCoreAsync();

            async Task AbortCoreAsync()
            {
                Debug.Assert(_controlStream != null && _remoteControlStream != null);

                var exception = new ConnectionClosedException();

                // Wait for operations on the control stream to complete to make sure it's safe to complete the control
                // stream output.
                await _controlStreamSemaphore.CompleteAndWaitAsync(exception).ConfigureAwait(false);

                await _controlStream.Output.CompleteAsync(exception).ConfigureAwait(false);
                await _remoteControlStream.Input.CompleteAsync(exception).ConfigureAwait(false);

                if (_waitForGoAwayFrame != null)
                {
                    await _waitForGoAwayFrame.Task.ConfigureAwait(false);
                }
            }
        }

        public async Task AcceptRequestsAsync(IConnection connection)
        {
            while (true)
            {
                // Accepts a new stream.
                IMultiplexedStream stream;
                try
                {
                    stream = await _networkConnection.AcceptStreamAsync(default).ConfigureAwait(false);
                }
                catch (ConnectionClosedException)
                {
                    // The peer closed the connection following graceful shutdown, we can just return.
                    return;
                }

                PipeReader? fieldsPipeReader = null;

                try
                {
                    ReadResult readResult = await stream.Input.ReadSegmentAsync(
                        SliceEncoding.Slice2,
                        _maxLocalHeaderSize,
                        CancellationToken.None).ConfigureAwait(false);

                    if (readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException("received icerpc request with empty header");
                    }

                    CancellationTokenSource? cancelDispatchSource = null;

                    lock (_mutex)
                    {
                        if (_isShuttingDown)
                        {
                            throw IceRpcStreamError.ConnectionShutdownByPeer.ToException();
                        }
                        else if (_isDisposed)
                        {
                            throw new ConnectionClosedException();
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

                            _streams.Add(stream);

                            stream.OnShutdown(() =>
                            {
                                lock (_mutex)
                                {
                                    _streams.Remove(stream);

                                    // If no more streams and shutting down, we can set the _streamsCompleted task
                                    // completion source as completed to allow shutdown to progress.
                                    if (_isShuttingDown && _streams.Count == 0)
                                    {
                                        _streamsCompleted.SetResult();
                                    }

                                    _cancelDispatchSources.Remove(cancelDispatchSource);
                                }

                                // If the stream is shutdown because the connection is aborted, make sure to cancel
                                // the dispatch.
                                if (_isDisposed)
                                {
                                    cancelDispatchSource.Cancel();
                                }

                                cancelDispatchSource.Dispose();
                            });
                        }
                    }

                    (IceRpcRequestHeader header, IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields, fieldsPipeReader) =
                        DecodeHeader(readResult.Buffer);
                    stream.Input.AdvanceTo(readResult.Buffer.End);

                    var request = new IncomingRequest(connection)
                    {
                        Fields = fields,
                        IsOneway = !stream.IsBidirectional,
                        Operation = header.Operation,
                        Path = header.Path,
                        Payload = stream.Input
                    };

                    Debug.Assert(cancelDispatchSource != null);
                    _ = Task.Run(() => DispatchRequestAsync(
                        request,
                        stream,
                        fieldsPipeReader,
                        cancelDispatchSource));
                }
                catch (Exception exception)
                {
                    if (fieldsPipeReader != null)
                    {
                        await fieldsPipeReader.CompleteAsync().ConfigureAwait(false);
                    }
                    await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
                    if (stream.IsBidirectional)
                    {
                        await stream.Output.CompleteAsync(exception).ConfigureAwait(false);
                    }

                    if (exception is MultiplexedStreamAbortedException streamAbortedException)
                    {
                        // The stream can be aborted if the invocation is canceled. It's not a fatal connection error,
                        // we can continue accepting new requests.
                        continue; // while (true)
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            async Task DispatchRequestAsync(
                IncomingRequest request,
                IMultiplexedStream stream,
                PipeReader? fieldsPipeReader,
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
                        // Expected if already disposed.
                    }
                });

                OutgoingResponse response;
                try
                {
                    response = await _dispatcher.DispatchAsync(
                        request,
                        cancelDispatchSource.Token).ConfigureAwait(false);

                    if (response != request.Response)
                    {
                        throw new InvalidOperationException(
                            "the dispatcher did not return the last response created for this request");
                    }
                }
                catch (Exception exception)
                {
                    // If we catch an exception, we return a failure response with a Slice-encoded payload.

                    if (exception is OperationCanceledException || exception is MultiplexedStreamAbortedException)
                    {
                        await stream.Output.CompleteAsync(
                            IceRpcStreamError.DispatchCanceled.ToException()).ConfigureAwait(false);

                        // We're done since the completion of the stream Output pipe writer aborted the stream or the
                        // stream was already aborted.
                        request.Complete();
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

                    // Attempt to encode this exception. If the encoding fails, we encode a DispatchException.
                    PipeReader responsePayload;
                    try
                    {
                        responsePayload = CreateExceptionPayload(request, remoteException);
                    }
                    catch (Exception encodeException)
                    {
                        // This should be extremely rare. For example, a middleware throwing a Slice1-only remote
                        // exception.
                        responsePayload = CreateExceptionPayload(
                            request,
                            new DispatchException(
                                message: null,
                                DispatchErrorCode.UnhandledException,
                                encodeException));
                    }
                    response = new OutgoingResponse(request)
                    {
                        Payload = responsePayload,
                        ResultType = ResultType.Failure
                    };

                    // Encode the retry policy into the fields of the new response.
                    if (remoteException.RetryPolicy != RetryPolicy.NoRetry)
                    {
                        RetryPolicy retryPolicy = remoteException.RetryPolicy;
                        response.Fields = response.Fields.With(
                            ResponseFieldKey.RetryPolicy,
                            (ref SliceEncoder encoder) => retryPolicy.Encode(ref encoder));
                    }

                    static PipeReader CreateExceptionPayload(IncomingRequest request, RemoteException exception)
                    {
                        ISliceEncodeFeature encodeFeature = request.Features.Get<ISliceEncodeFeature>() ??
                                SliceEncodeFeature.Default;

                        var pipe = new Pipe(encodeFeature.PipeOptions);

                        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
                        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
                        int startPos = encoder.EncodedByteCount;

                        // EncodeTrait throws for a Slice1-only exception.
                        exception.EncodeTrait(ref encoder);
                        SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
                        pipe.Writer.Complete(); // flush to reader and sets Is[Writer]Completed to true.
                        return pipe.Reader;
                    }
                }
                finally
                {
                    if (fieldsPipeReader != null)
                    {
                        await fieldsPipeReader.CompleteAsync().ConfigureAwait(false);

                        // The field values are now invalid - they point to potentially recycled and reused memory. We
                        // replace Fields by an empty dictionary to prevent accidental access to this reused memory.
                        request.Fields = ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty;
                    }

                    // Even when the code above throws an exception, we catch it and send a response. So we never want
                    // to give an exception to CompleteAsync when completing the incoming payload.
                    await request.Payload.CompleteAsync().ConfigureAwait(false);
                }

                if (request.IsOneway)
                {
                    request.Complete();
                    return;
                }

                try
                {
                    EncodeHeader();

                    // SendPayloadAsync takes care of the completion of the response payload, payload stream and stream
                    // output.
                    await SendPayloadAsync(response, stream, CancellationToken.None).ConfigureAwait(false);
                    request.Complete();
                }
                catch (Exception exception)
                {
                    request.Complete(exception);
                    await stream.Output.CompleteAsync(exception).ConfigureAwait(false);
                }

                void EncodeHeader()
                {
                    var encoder = new SliceEncoder(stream.Output, SliceEncoding.Slice2);

                    // Write the IceRpc response header.
                    Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(_headerSizeLength);
                    int headerStartPos = encoder.EncodedByteCount;

                    new IceRpcResponseHeader(response.ResultType).Encode(ref encoder);

                    encoder.EncodeDictionary(
                        response.Fields,
                        (ref SliceEncoder encoder, ResponseFieldKey key) => encoder.EncodeResponseFieldKey(key),
                        (ref SliceEncoder encoder, OutgoingFieldValue value) =>
                            value.Encode(ref encoder, _headerSizeLength));

                    // We're done with the header encoding, write the header size.
                    int headerSize = encoder.EncodedByteCount - headerStartPos;
                    CheckRemoteHeaderSize(headerSize);
                    SliceEncoder.EncodeVarUInt62((uint)headerSize, sizePlaceholder);
                }
            }

            static (IceRpcRequestHeader, IDictionary<RequestFieldKey, ReadOnlySequence<byte>>, PipeReader?) DecodeHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
                var header = new IceRpcRequestHeader(ref decoder);
                (IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields, PipeReader? pipeReader) =
                    DecodeFieldDictionary(ref decoder, (ref SliceDecoder decoder) => decoder.DecodeRequestFieldKey());

                return (header, fields, pipeReader);
            }
        }

        public void Dispose() => Abort(new ConnectionClosedException());

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            _networkConnection.HasCompatibleParams(remoteEndpoint);

        public async Task<IncomingResponse> InvokeAsync(
            OutgoingRequest request,
            IConnection connection,
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
                        if (_isShuttingDown || _isDisposed)
                        {
                            throw new ConnectionClosedException();
                        }
                        else
                        {
                            _streams.Add(stream);
                            ++_invocationCount;

                            stream.OnShutdown(() =>
                            {
                                lock (_mutex)
                                {
                                    --_invocationCount;

                                    _streams.Remove(stream);

                                    // If no more streams and shutting down, we can set the _streamsCompleted task
                                    // completion source as completed to allow shutdown to progress.
                                    if (_isShuttingDown && _streams.Count == 0)
                                    {
                                        _streamsCompleted.SetResult();
                                    }
                                }
                            });
                        }
                    }
                }

                EncodeHeader(stream.Output);

                // SendPayloadAsync takes care of the completion of the stream output.
                await SendPayloadAsync(request, stream, cancel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (stream != null)
                {
                    await stream.Output.CompleteAsync(exception).ConfigureAwait(false);
                    if (stream.IsBidirectional)
                    {
                        await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
                    }
                }

                if (exception is MultiplexedStreamAbortedException streamAbortedException)
                {
                    if (streamAbortedException.ErrorKind == MultiplexedStreamErrorKind.Protocol)
                    {
                        throw streamAbortedException.ToIceRpcException();
                    }
                    else
                    {
                        throw new ConnectionLostException(exception);
                    }
                }
                else
                {
                    // TODO: Should we wrap unexpected exceptions with ConnectionLostException?
                    throw;
                }
            }

            request.IsSent = true;

            if (request.IsOneway)
            {
                return new IncomingResponse(request, connection);
            }

            Debug.Assert(stream != null);
            try
            {
                ReadResult readResult = await stream.Input.ReadSegmentAsync(
                    SliceEncoding.Slice2,
                    _maxLocalHeaderSize,
                    cancel).ConfigureAwait(false);

                // Nothing cancels the stream input pipe reader.
                Debug.Assert(!readResult.IsCanceled);

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException($"received icerpc response with empty header");
                }

                (IceRpcResponseHeader header, IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields, PipeReader? fieldsPipeReader) =
                    DecodeHeader(readResult.Buffer);
                stream.Input.AdvanceTo(readResult.Buffer.End);

                return new IncomingResponse(request, connection, fields, fieldsPipeReader)
                {
                    Payload = stream.Input,
                    ResultType = header.ResultType
                };
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

                if (exception is MultiplexedStreamAbortedException streamAbortedException)
                {
                    if (streamAbortedException.ErrorKind == MultiplexedStreamErrorKind.Protocol)
                    {
                        throw streamAbortedException.ToIceRpcException();
                    }
                    else
                    {
                        throw new ConnectionLostException(exception);
                    }
                }
                else
                {
                    // TODO: Should we wrap unexpected exceptions with ConnectionLostException?
                    throw;
                }
            }

            void EncodeHeader(PipeWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);

                // Write the IceRpc request header.
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(_headerSizeLength);
                int headerStartPos = encoder.EncodedByteCount; // does not include the size

                var header = new IceRpcRequestHeader(request.Proxy.Path, request.Operation);

                header.Encode(ref encoder);

                encoder.EncodeDictionary(
                    request.Fields,
                    (ref SliceEncoder encoder, RequestFieldKey key) => encoder.EncodeRequestFieldKey(key),
                    (ref SliceEncoder encoder, OutgoingFieldValue value) =>
                        value.Encode(ref encoder, _headerSizeLength));

                // We're done with the header encoding, write the header size.
                int headerSize = encoder.EncodedByteCount - headerStartPos;
                CheckRemoteHeaderSize(headerSize);
                SliceEncoder.EncodeVarUInt62((uint)headerSize, sizePlaceholder);
            }

            static (IceRpcResponseHeader, IDictionary<ResponseFieldKey, ReadOnlySequence<byte>>, PipeReader?) DecodeHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
                var header = new IceRpcResponseHeader(ref decoder);

                (IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields, PipeReader? pipeReader) =
                    DecodeFieldDictionary(ref decoder, (ref SliceDecoder decoder) => decoder.DecodeResponseFieldKey());

                return (header, fields, pipeReader);
            }
        }

        public Task PingAsync(CancellationToken cancel) =>
            SendControlFrameAsync(IceRpcControlFrameType.Ping, encodeAction: null, cancel).AsTask();

        public async Task ShutdownAsync(string message, CancellationToken cancel)
        {
            if (_waitForGoAwayFrame == null)
            {
                throw new InvalidOperationException($"{nameof(ConnectAsync)} must be called first");
            }

            IceRpcGoAway? goAwayFrame = null;
            lock (_mutex)
            {
                // Mark the connection as shutting down to prevent further requests from being accepted. Shutdown might
                // already be initiated if both side initiated shutdown at the same time.
                if (!_isShuttingDown)
                {
                    _isShuttingDown = true;
                    if (_streams.Count == 0)
                    {
                        _streamsCompleted.SetResult();
                    }
                    goAwayFrame = new(_lastRemoteBidirectionalStreamId, _lastRemoteUnidirectionalStreamId, message);
                }
            }

            if (goAwayFrame != null)
            {
                // Send GoAway frame.
                await SendControlFrameAsync(
                    IceRpcControlFrameType.GoAway,
                    (ref SliceEncoder encoder) => goAwayFrame.Value.Encode(ref encoder),
                    CancellationToken.None).ConfigureAwait(false);
            }

            // If the shutdown is initiated locally, we wait for the peer to send back a GoAway frame. The task should
            // already be completed if the shutdown has been initiated by the peer.
            IceRpcGoAway peerGoAwayFrame = await _waitForGoAwayFrame.Task.ConfigureAwait(false);

            IEnumerable<IMultiplexedStream> invocations;
            lock (_mutex)
            {
                // Cancel the invocations that were not dispatched by the peer.
                invocations = _streams.Where(stream =>
                    !stream.IsRemote &&
                    (!stream.IsStarted || (stream.Id > (stream.IsBidirectional ?
                        peerGoAwayFrame.LastBidirectionalStreamId :
                        peerGoAwayFrame.LastUnidirectionalStreamId)))).ToArray();
            }

            // Abort streams for invocations that were not dispatched by the peer. The invocations will throw
            // ConnectionClosedException which is retryable.
            MultiplexedStreamAbortedException exception = IceRpcStreamError.ConnectionShutdownByPeer.ToException();
            foreach (IMultiplexedStream stream in invocations)
            {
                stream.Abort(exception);
            }

            // Wait for streams to complete.
            try
            {
                await _streamsCompleted.Task.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancel pending invocations and dispatches to speed up shutdown.

                IEnumerable<IMultiplexedStream> streams;
                IEnumerable<CancellationTokenSource> cancelDispatchSources;
                lock (_mutex)
                {
                    Debug.Assert(_isShuttingDown);
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

                // Abort streams for invocations.
                exception = IceRpcStreamError.ConnectionShutdown.ToException();
                foreach (IMultiplexedStream stream in streams)
                {
                    if (!stream.IsRemote)
                    {
                        stream.Abort(exception);
                    }
                }

                // Wait again for streams to complete.
                await _streamsCompleted.Task.ConfigureAwait(false);
            }

            try
            {
                await _controlStreamSemaphore.EnterAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await _controlStream!.Output.CompleteAsync().ConfigureAwait(false);
                    _ = await _remoteControlStream!.Input.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    _controlStreamSemaphore.Release();
                }

                await _networkConnection.ShutdownAsync(applicationErrorCode: 0, cancel).ConfigureAwait(false);
            }
            catch
            {
                // Ignore the connection got aborted concurrently.
            }
        }

        internal IceRpcProtocolConnection(IMultiplexedNetworkConnection networkConnection, ConnectionOptions options)
        {
            _networkConnection = networkConnection;
            _dispatcher = options.Dispatcher;
            _maxLocalHeaderSize = options.MaxIceRpcHeaderSize;
        }

        internal async Task ConnectAsync(CancellationToken cancel)
        {
            // Create the control stream and send the protocol Settings frame
            _controlStream = _networkConnection.CreateStream(false);

            var settings = new IceRpcSettings(
                _maxLocalHeaderSize == ConnectionOptions.DefaultMaxIceRpcHeaderSize ?
                    ImmutableDictionary<IceRpcSettingKey, ulong>.Empty :
                    new Dictionary<IceRpcSettingKey, ulong>
                    {
                        [IceRpcSettingKey.MaxHeaderSize] = (ulong)_maxLocalHeaderSize
                    });

            await SendControlFrameAsync(
                IceRpcControlFrameType.Settings,
                (ref SliceEncoder encoder) => settings.Encode(ref encoder),
                cancel).ConfigureAwait(false);

            // Wait for the remote control stream to be accepted and read the protocol Settings frame
            _remoteControlStream = await _networkConnection.AcceptStreamAsync(cancel).ConfigureAwait(false);

            await ReceiveControlFrameHeaderAsync(IceRpcControlFrameType.Settings, cancel).ConfigureAwait(false);
            await ReceiveSettingsFrameBody(cancel).ConfigureAwait(false);

            _waitForGoAwayFrame = new TaskCompletionSource<IceRpcGoAway>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Start a task to wait to receive the go away frame to initiate shutdown.
            _ = Task.Run(() => WaitForGoAwayAsync(), CancellationToken.None);
        }

        private static (IDictionary<TKey, ReadOnlySequence<byte>>, PipeReader?) DecodeFieldDictionary<TKey>(
            ref SliceDecoder decoder,
            DecodeFunc<TKey> decodeKeyFunc) where TKey : struct
        {
            int count = decoder.DecodeSize();

            IDictionary<TKey, ReadOnlySequence<byte>> fields;
            PipeReader? pipeReader;
            if (count == 0)
            {
                fields = ImmutableDictionary<TKey, ReadOnlySequence<byte>>.Empty;
                pipeReader = null;
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
            }
            else
            {
                // TODO: since this pipe is purely internal to the icerpc protocol implementation, it should be easy
                // to pool.
                var pipe = new Pipe();

                decoder.CopyTo(pipe.Writer);
                pipe.Writer.Complete();

                try
                {
                    _ = pipe.Reader.TryRead(out ReadResult readResult);
                    var fieldsDecoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);

                    fields = fieldsDecoder.DecodeShallowFieldDictionary(count, decodeKeyFunc);
                    fieldsDecoder.CheckEndOfBuffer(skipTaggedParams: false);

                    pipe.Reader.AdvanceTo(readResult.Buffer.Start); // complete read without consuming anything

                    pipeReader = pipe.Reader;
                }
                catch
                {
                    pipe.Reader.Complete();
                    throw;
                }
            }

            // The caller is responsible for completing the pipe reader.
            return (fields, pipeReader);
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

        private async ValueTask ReceiveSettingsFrameBody(CancellationToken cancel)
        {
            // We are still in the single-threaded initialization at this point.

            PipeReader input = _remoteControlStream!.Input;
            ReadResult readResult = await input.ReadSegmentAsync(
                SliceEncoding.Slice2,
                _maxLocalHeaderSize,
                cancel).ConfigureAwait(false);
            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            try
            {
                IceRpcSettings settings = SliceEncoding.Slice2.DecodeBuffer(
                    readResult.Buffer,
                    (ref SliceDecoder decoder) => new IceRpcSettings(ref decoder));

                if (settings.Value.TryGetValue(IceRpcSettingKey.MaxHeaderSize, out ulong value))
                {
                    // a varuint62 always fits in a long
                    _maxRemoteHeaderSize = ConnectionOptions.IceRpcCheckMaxHeaderSize((long)value);
                    _headerSizeLength = SliceEncoder.GetVarUInt62EncodedSize(value);
                }
                // all other settings are unknown and ignored
            }
            finally
            {
                input.AdvanceTo(readResult.Buffer.End);
            }
        }

        private async ValueTask<IceRpcGoAway> ReceiveGoAwayBodyAsync(CancellationToken cancel)
        {
            PipeReader input = _remoteControlStream!.Input;
            ReadResult readResult = await input.ReadSegmentAsync(
                SliceEncoding.Slice2,
                _maxLocalHeaderSize,
                cancel).ConfigureAwait(false);
            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            try
            {
                return SliceEncoding.Slice2.DecodeBuffer(
                    readResult.Buffer,
                    (ref SliceDecoder decoder) => new IceRpcGoAway(ref decoder));
            }
            finally
            {
                input.AdvanceTo(readResult.Buffer.End);
            }
        }

        private async ValueTask<FlushResult> SendControlFrameAsync(
            IceRpcControlFrameType frameType,
            EncodeAction? encodeAction,
            CancellationToken cancel)
        {
            await _controlStreamSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                PipeWriter output = _controlStream!.Output;
                output.GetSpan()[0] = (byte)frameType;
                output.Advance(1);

                if (encodeAction != null)
                {
                    EncodeFrame(output);
                }

                return await output.FlushAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                _controlStreamSemaphore.Release();
            }

            void EncodeFrame(IBufferWriter<byte> buffer)
            {
                var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(_headerSizeLength);
                int startPos = encoder.EncodedByteCount; // does not include the size
                encodeAction.Invoke(ref encoder);
                int headerSize = encoder.EncodedByteCount - startPos;
                CheckRemoteHeaderSize(headerSize);
                SliceEncoder.EncodeVarUInt62((uint)headerSize, sizePlaceholder);
            }
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
            PipeReader? payloadStream = outgoingFrame.PayloadStream;

            try
            {
                FlushResult flushResult = await CopyReaderToWriterAsync(
                    outgoingFrame.Payload,
                    payloadWriter,
                    endStream: payloadStream == null,
                    cancel).ConfigureAwait(false);

                if (flushResult.IsCompleted)
                {
                    // The remote reader gracefully completed the stream input pipe. We're done.
                    await payloadWriter.CompleteAsync().ConfigureAwait(false);

                    // We complete the payload and payload stream immediately. For example, we've just sent an outgoing
                    // request and we're waiting for the exception to come back.
                    await outgoingFrame.Payload.CompleteAsync().ConfigureAwait(false);
                    if (payloadStream != null)
                    {
                        await payloadStream.CompleteAsync().ConfigureAwait(false);
                    }
                    return;
                }
                else if (flushResult.IsCanceled)
                {
                    throw new InvalidOperationException(
                        "a payload writer is not allowed to return a canceled flush result");
                }
            }
            catch (Exception exception)
            {
                await payloadWriter.CompleteAsync(exception).ConfigureAwait(false);

                // An exception will trigger the immediate completion of the request and indirectly of the
                // outgoingFrame payloads.
                throw;
            }

            await outgoingFrame.Payload.CompleteAsync().ConfigureAwait(false);

            if (payloadStream == null)
            {
                await payloadWriter.CompleteAsync().ConfigureAwait(false);
            }
            else
            {
                // Send payloadStream in the background.
                outgoingFrame.PayloadStream = null; // we're now responsible for payloadStream

                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            FlushResult flushResult = await CopyReaderToWriterAsync(
                                payloadStream,
                                payloadWriter,
                                endStream: true,
                                CancellationToken.None).ConfigureAwait(false);

                            if (flushResult.IsCanceled)
                            {
                                throw new InvalidOperationException(
                                    "a payload writer interceptor is not allowed to return a canceled flush result");
                            }

                            await payloadStream.CompleteAsync().ConfigureAwait(false);
                            await payloadWriter.CompleteAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            await payloadStream.CompleteAsync(exception).ConfigureAwait(false);
                            await payloadWriter.CompleteAsync(exception).ConfigureAwait(false);
                        }
                    },
                    cancel);
            }

            async Task<FlushResult> CopyReaderToWriterAsync(
                PipeReader reader,
                PipeWriter writer,
                bool endStream,
                CancellationToken cancel)
            {
                // If the peer completes its input pipe reader, we cancel the pending read on the payload.
                stream.OnPeerInputCompleted(reader.CancelPendingRead);

                FlushResult flushResult;
                do
                {
                    ReadResult readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

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
                        throw new OperationCanceledException("payload pipe reader was canceled");
                    }
                } while (!flushResult.IsCanceled && !flushResult.IsCompleted);
                return flushResult;
            }
        }

        private void CheckRemoteHeaderSize(int headerSize)
        {
            if (headerSize > _maxRemoteHeaderSize)
            {
                throw new ProtocolException(
                    @$"header size ({headerSize
                    }) is greater than the remote peer's max header size ({_maxRemoteHeaderSize})");
            }
        }

        private async Task WaitForGoAwayAsync()
        {
            Debug.Assert(_waitForGoAwayFrame != null);
            try
            {
                // Receive and decode GoAway frame
                await ReceiveControlFrameHeaderAsync(
                    IceRpcControlFrameType.GoAway,
                    CancellationToken.None).ConfigureAwait(false);

                IceRpcGoAway goAwayFrame = await ReceiveGoAwayBodyAsync(CancellationToken.None).ConfigureAwait(false);

                _waitForGoAwayFrame.SetResult(goAwayFrame);

                // Call the peer shutdown initiated callback.
                PeerShutdownInitiated?.Invoke(goAwayFrame.Message);
            }
            catch (Exception exception)
            {
                _waitForGoAwayFrame.SetException(exception);
            }
        }
    }
}
