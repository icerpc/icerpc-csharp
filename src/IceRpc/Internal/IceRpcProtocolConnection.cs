// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal;

internal sealed class IceRpcProtocolConnection : ProtocolConnection
{
    public override ServerAddress ServerAddress => _transportConnection.ServerAddress;

    private Task? _acceptRequestsTask;
    private IConnectionContext? _connectionContext; // non-null once the connection is established
    private IMultiplexedStream? _controlStream;
    private int _dispatchCount;
    private readonly IDispatcher? _dispatcher;
    private readonly CancellationTokenSource _dispatchesAndInvocationsCts = new();
    private readonly TaskCompletionSource _dispatchesCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly SemaphoreSlim? _dispatchSemaphore;
    // The number of bytes we need to encode a size up to _maxRemoteHeaderSize. It's 2 for DefaultMaxHeaderSize.
    private int _headerSizeLength = 2;
    // Whether or not the inner exception details should be included in dispatch exceptions
    private readonly bool _includeInnerExceptionDetails;
    private bool _isReadOnly;
    private ulong? _lastRemoteBidirectionalStreamId;
    private ulong? _lastRemoteUnidirectionalStreamId;
    private readonly int _maxLocalHeaderSize;
    private int _maxRemoteHeaderSize = ConnectionOptions.DefaultMaxIceRpcHeaderSize;
    private readonly object _mutex = new();

    // Represents the streams of invocations where the corresponding request _may_ not been received or dispatched by
    // the peer yet.
    private readonly HashSet<IMultiplexedStream> _pendingInvocationStreams = new();
    private readonly IMultiplexedConnection _transportConnection;
    private Task<IceRpcGoAway>? _readGoAwayTask;
    private IMultiplexedStream? _remoteControlStream;
    private int _streamCount;
    private readonly TaskCompletionSource _streamsClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _tasksCts = new();

    internal IceRpcProtocolConnection(
        IMultiplexedConnection transportConnection,
        bool isServer,
        ConnectionOptions options)
        : base(isServer, options)
    {
        _transportConnection = transportConnection;
        _dispatcher = options.Dispatcher;
        _maxLocalHeaderSize = options.MaxIceRpcHeaderSize;

        if (options.MaxDispatches > 0)
        {
            _dispatchSemaphore = new SemaphoreSlim(
                initialCount: options.MaxDispatches,
                maxCount: options.MaxDispatches);
        }
        _includeInnerExceptionDetails = options.IncludeInnerExceptionDetails;
    }

    private protected override void CancelDispatchesAndInvocations()
    {
        if (!_dispatchesAndInvocationsCts.IsCancellationRequested)
        {
            _dispatchesAndInvocationsCts.Cancel();

            lock (_mutex)
            {
                _isReadOnly = true; // prevent new dispatches or invocations from being accepted.
                if (_streamCount == 0)
                {
                    _streamsClosed.TrySetResult();
                }
                if (_dispatchCount == 0)
                {
                    _dispatchesCompleted.TrySetResult();
                }
            }
        }
    }

    private protected override bool CheckIfIdle()
    {
        lock (_mutex)
        {
            // If idle, mark the connection as readonly to stop accepting new dispatches or invocations.
            if (!_isReadOnly && _streamCount == 0)
            {
                _isReadOnly = true;
                ConnectionClosedException = new(ConnectionErrorCode.ClosedByIdle);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    private protected override async Task<TransportConnectionInformation> ConnectAsyncCore(
        CancellationToken cancellationToken)
    {
        // Connect the transport connection
        TransportConnectionInformation transportConnectionInformation;
        try
        {
            transportConnectionInformation = await _transportConnection.ConnectAsync(
                cancellationToken).ConfigureAwait(false);
        }
        catch (TransportException exception) when (
            exception.ApplicationErrorCode is ulong errorCode &&
            errorCode == (ulong)IceRpcConnectionErrorCode.Refused)
        {
            ConnectionClosedException = new(
                ConnectionErrorCode.ClosedByPeer,
                "the connection establishment was refused");

            throw new ConnectionException(ConnectionErrorCode.ConnectRefused);
        }

        // This needs to be set before starting the accept requests task bellow.
        _connectionContext = new ConnectionContext(this, transportConnectionInformation);

        _controlStream = await _transportConnection.CreateStreamAsync(false, cancellationToken).ConfigureAwait(false);

        var settings = new IceRpcSettings(
            _maxLocalHeaderSize == ConnectionOptions.DefaultMaxIceRpcHeaderSize ?
                ImmutableDictionary<IceRpcSettingKey, ulong>.Empty :
                new Dictionary<IceRpcSettingKey, ulong>
                {
                    [IceRpcSettingKey.MaxHeaderSize] = (ulong)_maxLocalHeaderSize
                });

        await SendControlFrameAsync(
            IceRpcControlFrameType.Settings,
            settings.Encode,
            cancellationToken).ConfigureAwait(false);

        // Wait for the remote control stream to be accepted and read the protocol Settings frame
        _remoteControlStream = await _transportConnection.AcceptStreamAsync(
            cancellationToken).ConfigureAwait(false);

        await ReceiveControlFrameHeaderAsync(
            IceRpcControlFrameType.Settings,
            cancellationToken).ConfigureAwait(false);

        await ReceiveSettingsFrameBody(cancellationToken).ConfigureAwait(false);

        // Start a task to read the go away frame from the control stream and initiate shutdown.
        _readGoAwayTask = Task.Run(
            async () =>
            {
                try
                {
                    CancellationToken cancellationToken = _tasksCts.Token;
                    await ReceiveControlFrameHeaderAsync(
                        IceRpcControlFrameType.GoAway,
                        cancellationToken).ConfigureAwait(false);
                    IceRpcGoAway goAwayFrame = await ReceiveGoAwayBodyAsync(cancellationToken).ConfigureAwait(false);
                    InitiateShutdown(ConnectionErrorCode.ClosedByPeer);
                    return goAwayFrame;
                }
                catch (TransportException)
                {
                    throw; // The connection with the peer was lost.
                }
                catch (OperationCanceledException)
                {
                    throw; // The connection was disposed.
                }
                catch
                {
                    // Any other failure to read the GoAway frame is considered as a protocol violation. We kill the
                    // transport connection in this case.
                    await _transportConnection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            },
            CancellationToken.None);

        // Start a task that accepts requests (the "accept requests loop")
        _acceptRequestsTask = Task.Run(
            async () =>
            {
                try
                {
                    bool done = false;
                    do
                    {
                        // If _dispatcher is null, this call will block indefinitely until the connection is closed
                        // because the multiplexed connection MaxUnidirectionalStreams and MaxBidirectionalStreams
                        // options don't allow the peer to open streams.
                        IMultiplexedStream stream = await _transportConnection.AcceptStreamAsync(_tasksCts.Token)
                            .ConfigureAwait(false);

                        lock (_mutex)
                        {
                            if (_dispatchesCompleted.Task.IsCompleted || _streamsClosed.Task.IsCompleted)
                            {
                                // We can't accept any new stream or dispatch since the corresponding task is already
                                // completed. We'll close this stream with an exception (see below).
                                Debug.Assert(_isReadOnly);
                                done = true;
                            }
                            else
                            {
                                if (stream.IsBidirectional)
                                {
                                    if (_lastRemoteBidirectionalStreamId is null ||
                                        _lastRemoteBidirectionalStreamId.Value < stream.Id)
                                    {
                                        if (_isReadOnly)
                                        {
                                            done = true;
                                        }
                                        else
                                        {
                                            _lastRemoteBidirectionalStreamId = stream.Id;
                                        }
                                    }
                                    // else we received a smaller bidirectional stream ID that we're going to dispatch
                                    // even if we're shutting down.
                                }
                                else if (_lastRemoteUnidirectionalStreamId is null ||
                                        _lastRemoteUnidirectionalStreamId.Value < stream.Id)
                                {
                                    if (_isReadOnly)
                                    {
                                        done = true;
                                    }
                                    else
                                    {
                                        _lastRemoteUnidirectionalStreamId = stream.Id;
                                    }
                                }
                                // else we received a smaller unidirectional stream ID that we're going to dispatch even
                                // if we're shutting down.

                                if (!done)
                                {
                                    ++_dispatchCount;

                                    if (++_streamCount == 1)
                                    {
                                        // We were idle, we no longer are.
                                        DisableIdleCheck();
                                    }
                                    _ = RemoveStreamOnInputAndOutputClosedAsync(stream);
                                }
                            }
                        }

                        if (done)
                        {
                            await stream.Input.CompleteAsync(ConnectionClosedException).ConfigureAwait(false);
                            if (stream.IsBidirectional)
                            {
                                await stream.Output.CompleteAsync(ConnectionClosedException).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            _ = Task.Run(
                                async () =>
                                {
                                    try
                                    {
                                        await DispatchRequestAsync(stream).ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        lock (_mutex)
                                        {
                                            if (--_dispatchCount == 0 && _isReadOnly)
                                            {
                                                _dispatchesCompleted.TrySetResult();
                                            }
                                        }
                                    }
                                },
                                CancellationToken.None);
                        }
                    }
                    while (!done);
                }
                catch (OperationCanceledException)
                {
                    // Connection disposed.
                }
                catch (Exception exception)
                {
                    lock (_mutex)
                    {
                        if (_isReadOnly)
                        {
                            return; // already closing or closed
                        }

                        ConnectionClosedException = new(
                            ConnectionErrorCode.ClosedByAbort,
                            "the connection was lost",
                            exception);
                    }

                    ConnectionLost(exception);

                    // Don't wait for DisposeAsync to be called to cancel dispatches and invocations which might still
                    // be running.
                    CancelDispatchesAndInvocations();

                    // Also kill the transport connection right away instead of waiting DisposeAsync to be called.
                    await _transportConnection.DisposeAsync().ConfigureAwait(false);
                }
            },
            CancellationToken.None);

        return transportConnectionInformation;
    }

    private protected override async ValueTask DisposeAsyncCore()
    {
        // Cancel dispatches and invocations. This also sets _isReadOnly to true.
        CancelDispatchesAndInvocations();

        // Before disposing the transport connection, cancel pending tasks which are using the transport connection and
        // wait for the tasks to complete.
        _tasksCts.Cancel();
        try
        {
            await Task.WhenAll(
                _acceptRequestsTask ?? Task.CompletedTask,
                _readGoAwayTask ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch
        {
            // Ignore, we don't care if the tasks fail here (ReadGoAwayTask can fail if the connection is lost).
        }

        // Dispose the transport connection. This will abort the transport connection if it wasn't shutdown first.
        await _transportConnection.DisposeAsync().ConfigureAwait(false);

        // Next, wait for dispatches to complete. We're not waiting for network activity on the streams to complete
        // (with _streamClosed.Task). It should be complete since we've disposed the underlying transport connection.
        await _dispatchesCompleted.Task.ConfigureAwait(false);

        _tasksCts.Dispose();
        _dispatchesAndInvocationsCts.Dispose();
        _dispatchSemaphore?.Dispose();
    }

    private protected override async Task<IncomingResponse> InvokeAsyncCore(
        OutgoingRequest request,
        CancellationToken cancellationToken)
    {
        IMultiplexedStream? stream = null;
        using var invocationCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _dispatchesAndInvocationsCts.Token);

        Exception? completeException = null;
        try
        {
            if (request.ServiceAddress.Fragment.Length > 0)
            {
                throw new NotSupportedException("the icerpc protocol does not support fragments");
            }

            // Create the stream.
            stream = await _transportConnection.CreateStreamAsync(
                bidirectional: !request.IsOneway,
                invocationCts.Token).ConfigureAwait(false);

            // Keep track of the invocation for the shutdown logic.
            lock (_mutex)
            {
                if (_isReadOnly)
                {
                    // Don't process the invocation if the connection is in the process of shutting down or it's
                    // already closed.
                    Debug.Assert(ConnectionClosedException is not null);
                    throw ConnectionClosedException;
                }
                else
                {
                    if (++_streamCount == 1)
                    {
                        DisableIdleCheck();
                    }
                    _pendingInvocationStreams.Add(stream);

                    _ = RemoveStreamOnInputAndOutputClosedAsync(stream);
                }
            }

            EncodeHeader(stream.Output);

            // SendPayloadAsync takes care of the completion of the stream output.
            await SendPayloadAsync(request, stream, invocationCts.Token).ConfigureAwait(false);

            if (request.IsOneway)
            {
                return new IncomingResponse(request, _connectionContext!);
            }

            ReadResult readResult = await stream.Input.ReadSegmentAsync(
                SliceEncoding.Slice2,
                _maxLocalHeaderSize,
                invocationCts.Token).ConfigureAwait(false);

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    // We received a response, it's no longer a pending invocation.
                    _ = _pendingInvocationStreams.Remove(stream);
                }
            }

            // Nothing cancels the stream input pipe reader.
            Debug.Assert(!readResult.IsCanceled);

            if (readResult.Buffer.IsEmpty)
            {
                throw new InvalidDataException("received icerpc response with empty header");
            }

            (IceRpcResponseHeader header, IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields, PipeReader? fieldsPipeReader) =
                DecodeHeader(readResult.Buffer);
            stream.Input.AdvanceTo(readResult.Buffer.End);

            return new IncomingResponse(request, _connectionContext!, fields, fieldsPipeReader)
            {
                Payload = stream.Input,
                ResultType = header.ResultType
            };
        }
        catch (OperationCanceledException exception)
        {
            if (_dispatchesAndInvocationsCts.IsCancellationRequested)
            {
                completeException = exception;
                throw new ConnectionException(ConnectionErrorCode.OperationAborted);
            }
            else
            {
                completeException = exception;
                throw;
            }
        }
        catch (IceRpcProtocolStreamException exception)
        {
            completeException = exception;
            throw;
        }
        catch (ConnectionException exception)
        {
            completeException = exception;
            throw;
        }
        catch (ProtocolException exception)
        {
            // TODO: should we throw IceRpcProtocolStreamException(IceRpcStreamErrorCode.ProtocolError) instead?
            completeException = exception;
            throw;
        }
        catch (TransportException exception)
        {
            completeException = exception;
            throw new ConnectionException(ConnectionErrorCode.TransportError, exception);
        }
        catch (Exception exception)
        {
            completeException = exception;
            throw new ConnectionException(ConnectionErrorCode.Unspecified, exception);
        }
        finally
        {
            if (stream is not null && completeException is not null)
            {
                await stream.Output.CompleteAsync(completeException).ConfigureAwait(false);
                if (stream.IsBidirectional)
                {
                    await stream.Input.CompleteAsync(completeException).ConfigureAwait(false);
                }
            }
        }

        void EncodeHeader(PipeWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);

            // Write the IceRpc request header.
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(_headerSizeLength);
            int headerStartPos = encoder.EncodedByteCount; // does not include the size

            var header = new IceRpcRequestHeader(request.ServiceAddress.Path, request.Operation);

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

    private protected override async Task ShutdownAsyncCore(CancellationToken cancellationToken)
    {
        Debug.Assert(ConnectionClosedException is not null);

        if (_readGoAwayTask is null)
        {
            // No stream or dispatch if the connection is not connected.
            Debug.Assert(_streamCount == 0 && _dispatchCount == 0);

            // Calling shutdown before connect indicates that the connection establishment is refused.
            await _transportConnection.CloseAsync(
                (ulong)IceRpcConnectionErrorCode.Refused,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // If the connection is connected, exchange go away frames with the peer.

            IceRpcGoAway goAwayFrame;
            lock (_mutex)
            {
                _isReadOnly = true;
                if (_streamCount == 0)
                {
                    _streamsClosed.TrySetResult();
                }
                if (_dispatchCount == 0)
                {
                    _dispatchesCompleted.TrySetResult();
                }
                goAwayFrame = new(_lastRemoteBidirectionalStreamId, _lastRemoteUnidirectionalStreamId);
            }

            await SendControlFrameAsync(
                IceRpcControlFrameType.GoAway,
                goAwayFrame.Encode,
                cancellationToken).ConfigureAwait(false);

            // Wait for the peer to send back a GoAway frame. The task should already be completed if the shutdown has
            // been initiated by the peer.
            IceRpcGoAway peerGoAwayFrame = await _readGoAwayTask!.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Abort streams for outgoing requests that were not dispatched by the peer. The invocations will throw
            // ConnectionClosedException which can be retried. Since _isReadOnly is true, _pendingInvocationStreams is
            // read-only at this point.
            foreach (IMultiplexedStream stream in _pendingInvocationStreams.Where(stream =>
                !stream.IsStarted ||
                (stream.IsBidirectional ?
                    peerGoAwayFrame.LastBidirectionalStreamId is null ||
                    stream.Id > peerGoAwayFrame.LastBidirectionalStreamId :
                        peerGoAwayFrame.LastUnidirectionalStreamId is null ||
                        stream.Id > peerGoAwayFrame.LastUnidirectionalStreamId)))
            {
                stream.Abort(ConnectionClosedException);
            }

            // Wait for dispatches to complete and network activity on streams (other than control streams) to cease.
            await Task.WhenAll(_dispatchesCompleted.Task, _streamsClosed.Task).WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            // Close the control stream to notify the peer that on our side, all the streams completed.
            _controlStream!.Output.Complete();

            // Wait for the peer notification that on its side all the streams are completed. It's important to wait for
            // this notification before closing the connection. In particular with Quic where closing the connection
            // before all the streams are processed could lead to a stream failure.
            try
            {
                // Wait for the _remoteControlStream Input completion.
                ReadResult readResult = await _remoteControlStream!.Input.ReadAsync(cancellationToken)
                    .ConfigureAwait(false);

                Debug.Assert(!readResult.IsCanceled);

                if (!readResult.IsCompleted || !readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException("received bytes on the control stream after the GoAway frame");
                }
            }
            catch (TransportException exception) when (
                exception.ErrorCode == TransportErrorCode.ConnectionClosed &&
                exception.ApplicationErrorCode is ulong errorCode &&
                (IceRpcConnectionErrorCode)errorCode == IceRpcConnectionErrorCode.NoError)
            {
                // Expected if the peer closed the connection first.
            }

            // We can now safely close the connection.
            await _transportConnection.CloseAsync(
                (ulong)IceRpcConnectionErrorCode.NoError,
                cancellationToken).ConfigureAwait(false);
        }
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

    /// <summary>Sends the payload and payload continuation of an outgoing frame. SendPayloadAsync completes the payload
    /// if successful. It completes the output only if there's no payload continuation. Otherwise, it starts a streaming
    /// task that is responsible for completing the payload continuation and the output.</summary>
    private static async ValueTask SendPayloadAsync(
        OutgoingFrame outgoingFrame,
        IMultiplexedStream stream,
        CancellationToken cancellationToken)
    {
        PipeWriter payloadWriter = outgoingFrame.GetPayloadWriter(stream.Output);
        PipeReader? payloadContinuation = outgoingFrame.PayloadContinuation;

        try
        {
            FlushResult flushResult = await CopyReaderToWriterAsync(
                outgoingFrame.Payload,
                payloadWriter,
                endStream: payloadContinuation is null,
                cancellationToken).ConfigureAwait(false);

            if (flushResult.IsCompleted)
            {
                // The remote reader gracefully completed the stream input pipe. We're done.
                await payloadWriter.CompleteAsync().ConfigureAwait(false);

                // We complete the payload and payload continuation immediately. For example, we've just sent an outgoing
                // request and we're waiting for the exception to come back.
                await outgoingFrame.Payload.CompleteAsync().ConfigureAwait(false);
                if (payloadContinuation is not null)
                {
                    await payloadContinuation.CompleteAsync().ConfigureAwait(false);
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

        if (payloadContinuation is null)
        {
            await payloadWriter.CompleteAsync().ConfigureAwait(false);
        }
        else
        {
            // Send payloadContinuation in the background.
            outgoingFrame.PayloadContinuation = null; // we're now responsible for payloadContinuation

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        FlushResult flushResult = await CopyReaderToWriterAsync(
                            payloadContinuation,
                            payloadWriter,
                            endStream: true,
                            CancellationToken.None).ConfigureAwait(false);

                        if (flushResult.IsCanceled)
                        {
                            throw new InvalidOperationException(
                                "a payload writer interceptor is not allowed to return a canceled flush result");
                        }

                        await payloadContinuation.CompleteAsync().ConfigureAwait(false);
                        await payloadWriter.CompleteAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        await payloadContinuation.CompleteAsync(exception).ConfigureAwait(false);
                        await payloadWriter.CompleteAsync(exception).ConfigureAwait(false);
                    }
                },
                cancellationToken);
        }

        async Task<FlushResult> CopyReaderToWriterAsync(
            PipeReader reader,
            PipeWriter writer,
            bool endStream,
            CancellationToken cancellationToken)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // If the peer is no longer reading the payload, call Cancel on readCts.
            Task cancelOnOutputClosedTask = CancelOnOutputClosedAsync(readCts);

            FlushResult flushResult;

            try
            {
                ReadResult readResult;
                do
                {
                    try
                    {
                        readResult = await reader.ReadAsync(readCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stream.OutputClosed.IsCompleted)
                    {
                        // This either throws the OutputClosed exception or returns a completed FlushResult.
                        return await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                    }

                    if (readResult.IsCanceled)
                    {
                        // The application (or an interceptor/middleware) called CancelPendingRead on reader.
                        reader.AdvanceTo(readResult.Buffer.Start); // Did not consume any byte in reader.

                        // We complete without throwing/catching any exception.
                        await writer.CompleteAsync(new IceRpcProtocolStreamException(IceRpcStreamErrorCode.Canceled))
                            .ConfigureAwait(false);

                        flushResult = new FlushResult(isCanceled: false, isCompleted: true);
                    }
                    else
                    {
                        try
                        {
                            flushResult = await writer.WriteAsync(
                                readResult.Buffer,
                                readResult.IsCompleted && endStream,
                                cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            reader.AdvanceTo(readResult.Buffer.End);
                        }
                    }
                }
                while (!readResult.IsCompleted && !flushResult.IsCanceled && !flushResult.IsCompleted);
            }
            finally
            {
                readCts.Cancel();
                await cancelOnOutputClosedTask.ConfigureAwait(false);
            }

            return flushResult;

            async Task CancelOnOutputClosedAsync(CancellationTokenSource readCts)
            {
                try
                {
                    await stream.OutputClosed.WaitAsync(readCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore the reason of the writes close, or the OperationCanceledException
                }

                readCts.Cancel();
            }
        }
    }

    private void CheckRemoteHeaderSize(int headerSize)
    {
        if (headerSize > _maxRemoteHeaderSize)
        {
            throw new ProtocolException(
                $"header size ({headerSize}) is greater than the remote peer's max header size ({_maxRemoteHeaderSize})");
        }
    }

    private async Task DispatchRequestAsync(IMultiplexedStream stream)
    {
        using var dispatchCts = new CancellationTokenSource();

        if (stream.IsBidirectional)
        {
            // If the peer is no longer interested in the response of the dispatch, we cancel the dispatch.
            _ = CancelDispatchOnOutputClosedAsync();
        }

        // Cancel the dispatch cancellation token source if dispatches and invocations are canceled.
        using CancellationTokenRegistration tokenRegistration =
            _dispatchesAndInvocationsCts.Token.UnsafeRegister(
                cts => ((CancellationTokenSource)cts!).Cancel(),
                dispatchCts);

        CancellationToken cancellationToken = dispatchCts.Token;

        PipeReader? fieldsPipeReader = null;

        try
        {
            ReadResult readResult = await stream.Input.ReadSegmentAsync(
                SliceEncoding.Slice2,
                _maxLocalHeaderSize,
                cancellationToken).ConfigureAwait(false);

            if (readResult.Buffer.IsEmpty)
            {
                throw new InvalidDataException("received icerpc request with empty header");
            }

            (IceRpcRequestHeader header, IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields, fieldsPipeReader) =
                DecodeHeader(readResult.Buffer);
            stream.Input.AdvanceTo(readResult.Buffer.End);

            using var request = new IncomingRequest(_connectionContext!)
            {
                Fields = fields,
                IsOneway = !stream.IsBidirectional,
                Operation = header.Operation,
                Path = header.Path,
                Payload = stream.Input
            };

            await PerformDispatchRequestAsync(request).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (fieldsPipeReader is not null)
            {
                await fieldsPipeReader.CompleteAsync().ConfigureAwait(false);
            }

            await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
            if (stream.IsBidirectional)
            {
                await stream.Output.CompleteAsync(exception).ConfigureAwait(false);
            }

            throw;
        }

        async Task CancelDispatchOnOutputClosedAsync()
        {
            try
            {
                await stream.OutputClosed.ConfigureAwait(false);
            }
            catch
            {
                // Ignore the reason of the writes close.
            }

            try
            {
                dispatchCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Expected if already disposed.
            }
        }

        async Task PerformDispatchRequestAsync(IncomingRequest request)
        {
            Debug.Assert(_dispatcher is not null);

            OutgoingResponse response;

            try
            {
                if (_dispatchSemaphore is SemaphoreSlim dispatchSemaphore)
                {
                    await dispatchSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    response = await _dispatcher.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _dispatchSemaphore?.Release();
                }

                if (response != request.Response)
                {
                    var exception = new InvalidOperationException(
                        "the dispatcher did not return the last response created for this request");

                    await response.Payload.CompleteAsync(exception).ConfigureAwait(false);
                    if (response.PayloadContinuation is PipeReader payloadContinuation)
                    {
                        await payloadContinuation.CompleteAsync(exception).ConfigureAwait(false);
                    }
                    throw exception;
                }
            }
            catch when (request.IsOneway || _tasksCts.IsCancellationRequested)
            {
                // No reply for oneway requests or if the connection is disposed.
                return;
            }
            catch (OperationCanceledException exception) when (cancellationToken == exception.CancellationToken)
            {
                await stream.Output.CompleteAsync((Exception?)ConnectionClosedException ?? exception)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception exception)
            {
                // If we catch an exception, we return a failure response with a dispatch exception payload.

                if (exception is not DispatchException dispatchException || dispatchException.ConvertToUnhandled)
                {
                    DispatchErrorCode errorCode = exception switch
                    {
                        InvalidDataException _ => DispatchErrorCode.InvalidData,
                        IceRpcProtocolStreamException => DispatchErrorCode.StreamError,
                        _ => DispatchErrorCode.UnhandledException
                    };

                    // We pass null for message to get the message computed from the exception by DefaultMessage.
                    dispatchException = new DispatchException(
                        message: null,
                        errorCode,
                        _includeInnerExceptionDetails ? exception : null);
                }

                response = new OutgoingResponse(request)
                {
                    Payload = CreateDispatchExceptionPayload(request, dispatchException),
                    ResultType = ResultType.Failure
                };

                // Encode the retry policy into the fields of the new response.
                if (dispatchException.RetryPolicy != RetryPolicy.NoRetry)
                {
                    RetryPolicy retryPolicy = dispatchException.RetryPolicy;
                    response.Fields = response.Fields.With(
                        ResponseFieldKey.RetryPolicy,
                        (ref SliceEncoder encoder) => retryPolicy.Encode(ref encoder));
                }

                static PipeReader CreateDispatchExceptionPayload(IncomingRequest request, DispatchException exception)
                {
                    SliceEncodeOptions encodeOptions = request.Features.Get<ISliceFeature>()?.EncodeOptions ??
                        SliceEncodeOptions.Default;

                    var pipe = new Pipe(encodeOptions.PipeOptions);
                    var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
                    Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
                    int startPos = encoder.EncodedByteCount;
                    encoder.EncodeDispatchException(exception);
                    SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
                    pipe.Writer.Complete(); // flush to reader and sets Is[Writer]Completed to true.
                    return pipe.Reader;
                }
            }
            finally
            {
                if (fieldsPipeReader is not null)
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
                return;
            }

            try
            {
                EncodeHeader();

                // SendPayloadAsync takes care of the completion of the response payload, payload continuation and stream
                // output.
                await SendPayloadAsync(response, stream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
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

    private async ValueTask ReceiveControlFrameHeaderAsync(
        IceRpcControlFrameType expectedFrameType,
        CancellationToken cancellationToken)
    {
        PipeReader input = _remoteControlStream!.Input;

        while (true)
        {
            ReadResult readResult = await input.ReadAsync(cancellationToken).ConfigureAwait(false);

            // We don't call CancelPendingRead on _remoteControlStream.Input.
            Debug.Assert(!readResult.IsCanceled);

            if (readResult.Buffer.IsEmpty)
            {
                throw new InvalidDataException("invalid empty control frame");
            }

            if (TryDecodeFrameType(readResult.Buffer, out IceRpcControlFrameType frameType, out long consumed))
            {
                input.AdvanceTo(readResult.Buffer.GetPosition(consumed));
                break; // while
            }
            else
            {
                input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            }
        }

        bool TryDecodeFrameType(ReadOnlySequence<byte> buffer, out IceRpcControlFrameType frameType, out long consumed)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            if (decoder.TryDecodeUInt8(out byte value))
            {
                frameType = value.AsIceRpcControlFrameType();
                if (frameType != expectedFrameType)
                {
                    throw new InvalidDataException(
                       $"received frame type {frameType} but expected {expectedFrameType}");
                }
                consumed = decoder.Consumed;
                return true;
            }
            else
            {
                frameType = IceRpcControlFrameType.GoAway;
                consumed = 0;
                return false;
            }
        }
    }

    private async ValueTask<IceRpcGoAway> ReceiveGoAwayBodyAsync(CancellationToken cancellationToken)
    {
        PipeReader input = _remoteControlStream!.Input;
        ReadResult readResult = await input.ReadSegmentAsync(
            SliceEncoding.Slice2,
            _maxLocalHeaderSize,
            cancellationToken).ConfigureAwait(false);

        // We don't call CancelPendingRead on _remoteControlStream.Input
        Debug.Assert(!readResult.IsCanceled);

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

    private async ValueTask ReceiveSettingsFrameBody(CancellationToken cancellationToken)
    {
        // We are still in the single-threaded initialization at this point.

        PipeReader input = _remoteControlStream!.Input;
        ReadResult readResult = await input.ReadSegmentAsync(
            SliceEncoding.Slice2,
            _maxLocalHeaderSize,
            cancellationToken).ConfigureAwait(false);

        // We don't call CancelPendingRead on _remoteControlStream.Input
        Debug.Assert(!readResult.IsCanceled);

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

    private async Task RemoveStreamOnInputAndOutputClosedAsync(IMultiplexedStream stream)
    {
        try
        {
            await Task.WhenAll(stream.InputClosed, stream.OutputClosed).ConfigureAwait(false);
        }
        catch
        {
            // Ignore the reason of the Input/Output closure
        }

        lock (_mutex)
        {
            if (!stream.IsRemote && !_isReadOnly)
            {
                _ = _pendingInvocationStreams.Remove(stream);
            }

            if (--_streamCount == 0)
            {
                if (_isReadOnly)
                {
                    _streamsClosed.TrySetResult();
                }
                else
                {
                    EnableIdleCheck();
                }
            }
        }
    }

    private ValueTask<FlushResult> SendControlFrameAsync(
        IceRpcControlFrameType frameType,
        EncodeAction encodeAction,
        CancellationToken cancellationToken)
    {
        PipeWriter output = _controlStream!.Output;

        EncodeFrame(output);

        return output.FlushAsync(cancellationToken); // Flush

        void EncodeFrame(IBufferWriter<byte> buffer)
        {
            var encoder = new SliceEncoder(buffer, SliceEncoding.Slice2);
            encoder.EncodeIceRpcControlFrameType(frameType);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(_headerSizeLength);
            int startPos = encoder.EncodedByteCount; // does not include the size
            encodeAction.Invoke(ref encoder);
            int headerSize = encoder.EncodedByteCount - startPos;
            CheckRemoteHeaderSize(headerSize);
            SliceEncoder.EncodeVarUInt62((uint)headerSize, sizePlaceholder);
        }
    }
}
