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
    private readonly HashSet<IMultiplexedStream> _localStreams = new();
    private readonly TaskCompletionSource _localStreamsCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly int _maxLocalHeaderSize;
    private int _maxRemoteHeaderSize = ConnectionOptions.DefaultMaxIceRpcHeaderSize;
    private readonly object _mutex = new();
    private readonly IMultiplexedConnection _transportConnection;
    private Task<IceRpcGoAway>? _readGoAwayTask;
    private IMultiplexedStream? _remoteControlStream;
    private int _streamCount;
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
                if (_localStreams.Count == 0)
                {
                    _localStreamsCompleted.TrySetResult();
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
            if (_streamCount == 0)
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

        // Start a task to start accepting requests.
        _acceptRequestsTask = Task.Run(
            async () =>
            {
                try
                {
                    while (true)
                    {
                        if (_dispatchSemaphore is SemaphoreSlim dispatchSemaphore)
                        {
                            await dispatchSemaphore.WaitAsync(_tasksCts.Token).ConfigureAwait(false);
                        }

                        IMultiplexedStream stream;

                        try
                        {
                            // If _dispatcher is null, this call will be block indefinitely until the connection is
                            // closed because the multiplexed connection MaxUnidirectionalStreams and
                            // MaxBidirectionalStreams options don't allow the peer to open streams.
                            stream = await _transportConnection.AcceptStreamAsync(_tasksCts.Token)
                                .ConfigureAwait(false);
                        }
                        catch
                        {
                            _dispatchSemaphore?.Release();
                            throw;
                        }

                        try
                        {
                            // AcceptRequestAsync is responsible to release the dispatch semaphore.
                            await AcceptRequestAsync(stream, _tasksCts.Token).ConfigureAwait(false);
                        }
                        catch (IceRpcProtocolStreamException)
                        {
                            // A stream failure is not a fatal connection error; we can continue accepting new requests.
                        }
                    }
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

        // Abort all local streams since we're waiting for their completion below.
        var exception = new ConnectionException(ConnectionErrorCode.OperationAborted);
        foreach (IMultiplexedStream stream in _localStreams)
        {
            stream.Abort(exception);
        }
        // Cancel dispatches and invocations.
        CancelDispatchesAndInvocations();

        // Dispose the transport connection. This will abort the transport connection if it wasn't shutdown first.
        await _transportConnection.DisposeAsync().ConfigureAwait(false);

        // Next, wait for dispatches and local streams to complete (local streams are used as an approximation for
        // invocations).
        await Task.WhenAll(_dispatchesCompleted.Task, _localStreamsCompleted.Task).ConfigureAwait(false);

        _tasksCts.Dispose();
        _dispatchesAndInvocationsCts.Dispose();
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
                    if (_streamCount++ == 0)
                    {
                        DisableIdleCheck();
                    }
                    _localStreams.Add(stream);

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
            // No invocation or dispatch if the connection is not connected.
            Debug.Assert(_localStreams.Count == 0 && _dispatchCount == 0);

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
                if (_localStreams.Count == 0)
                {
                    _localStreamsCompleted.TrySetResult();
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

            // Abort streams for requests that were not dispatched by the peer. The invocations will throw
            // ConnectionClosedException which can be retried.
            IEnumerable<IMultiplexedStream> invocations;
            lock (_mutex)
            {
                invocations = _localStreams.Where(stream =>
                    !stream.IsStarted ||
                    (stream.IsBidirectional ?
                        peerGoAwayFrame.LastBidirectionalStreamId is null ||
                        stream.Id > peerGoAwayFrame.LastBidirectionalStreamId :
                            peerGoAwayFrame.LastUnidirectionalStreamId is null ||
                            stream.Id > peerGoAwayFrame.LastUnidirectionalStreamId)).ToArray();
            }

            foreach (IMultiplexedStream stream in invocations)
            {
                stream.Abort(ConnectionClosedException);
            }

            // Wait for dispatches and local streams to complete.
            await Task.WhenAll(_dispatchesCompleted.Task, _localStreamsCompleted.Task).WaitAsync(cancellationToken)
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

    /// <summary>Sends the payload and payload stream of an outgoing frame. SendPayloadAsync completes the payload
    /// if successful. It completes the output only if there's no payload stream. Otherwise, it starts a streaming
    /// task that is responsible for completing the payload stream and the output.</summary>
    private static async ValueTask SendPayloadAsync(
        OutgoingFrame outgoingFrame,
        IMultiplexedStream stream,
        CancellationToken cancellationToken)
    {
        PipeWriter payloadWriter = outgoingFrame.GetPayloadWriter(stream.Output);
        PipeReader? payloadStream = outgoingFrame.PayloadStream;

        try
        {
            FlushResult flushResult = await CopyReaderToWriterAsync(
                outgoingFrame.Payload,
                payloadWriter,
                endStream: payloadStream is null,
                cancellationToken).ConfigureAwait(false);

            if (flushResult.IsCompleted)
            {
                // The remote reader gracefully completed the stream input pipe. We're done.
                await payloadWriter.CompleteAsync().ConfigureAwait(false);

                // We complete the payload and payload stream immediately. For example, we've just sent an outgoing
                // request and we're waiting for the exception to come back.
                await outgoingFrame.Payload.CompleteAsync().ConfigureAwait(false);
                if (payloadStream is not null)
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

        if (payloadStream is null)
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
            Task cancelOnWritesClosedTask = CancelOnWritesClosedAsync(readCts);

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
                        // This either throws the WritesClosed exception or returns a completed FlushResult.
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
                await cancelOnWritesClosedTask.ConfigureAwait(false);
            }

            return flushResult;

            async Task CancelOnWritesClosedAsync(CancellationTokenSource readCts)
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

    private async Task AcceptRequestAsync(IMultiplexedStream stream, CancellationToken cancellationToken)
    {
        Debug.Assert(_dispatcher is not null);

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

            lock (_mutex)
            {
                if (_isReadOnly)
                {
                    Debug.Assert(ConnectionClosedException is not null);
                    throw ConnectionClosedException;
                }
                else
                {
                    ++_dispatchCount;
                    if (stream.IsBidirectional)
                    {
                        _lastRemoteBidirectionalStreamId = stream.Id;
                    }
                    else
                    {
                        _lastRemoteUnidirectionalStreamId = stream.Id;
                    }

                    if (_streamCount++ == 0)
                    {
                        DisableIdleCheck();
                    }
                    _ = RemoveStreamOnInputAndOutputClosedAsync(stream);
                }
            }

            (IceRpcRequestHeader header, IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields, fieldsPipeReader) =
                DecodeHeader(readResult.Buffer);
            stream.Input.AdvanceTo(readResult.Buffer.End);

            _ = Task.Run(
                async () =>
                {
                    using var request = new IncomingRequest(_connectionContext!)
                    {
                        Fields = fields,
                        IsOneway = !stream.IsBidirectional,
                        Operation = header.Operation,
                        Path = header.Path,
                        Payload = stream.Input
                    };
                    await DispatchRequestAsync(request, stream, fieldsPipeReader).ConfigureAwait(false);
                },
                CancellationToken.None);
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

            _dispatchSemaphore?.Release();

            throw;
        }

        async Task DispatchRequestAsync(
            IncomingRequest request,
            IMultiplexedStream stream,
            PipeReader? fieldsPipeReader)
        {
            using var dispatchCts = new CancellationTokenSource();

            if (!request.IsOneway)
            {
                // If the peer is no longer interested in the response of the dispatch, we cancel the dispatch.
                _ = CancelDispatchOnOutputClosedAsync();
            }

            // Cancel the dispatch cancellation token source if dispatches and invocations are canceled.
            using CancellationTokenRegistration tokenRegistration =
                _dispatchesAndInvocationsCts.Token.UnsafeRegister(
                    cts => ((CancellationTokenSource)cts!).Cancel(),
                    dispatchCts);

            OutgoingResponse response;
            try
            {
                response = await _dispatcher.DispatchAsync(request, dispatchCts.Token).ConfigureAwait(false);

                if (response != request.Response)
                {
                    var exception = new InvalidOperationException(
                        "the dispatcher did not return the last response created for this request");

                    await response.Payload.CompleteAsync(exception).ConfigureAwait(false);
                    if (response.PayloadStream is PipeReader payloadStream)
                    {
                        await payloadStream.CompleteAsync(exception).ConfigureAwait(false);
                    }
                    throw exception;
                }
            }
            catch when (request.IsOneway || _tasksCts.IsCancellationRequested)
            {
                // No reply for oneway requests or if the connection is disposed.
                return;
            }
            catch (OperationCanceledException exception) when (dispatchCts.Token == exception.CancellationToken)
            {
                await stream.Output.CompleteAsync((Exception?)ConnectionClosedException ?? exception)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception exception)
            {
                // If we catch an exception, we return a failure response with a Slice-encoded payload.

                if (exception is not RemoteException remoteException || remoteException.ConvertToUnhandled)
                {
                    DispatchErrorCode errorCode = exception switch
                    {
                        InvalidDataException _ => DispatchErrorCode.InvalidData,
                        IceRpcProtocolStreamException => DispatchErrorCode.StreamError,
                        _ => DispatchErrorCode.UnhandledException
                    };

                    // We pass null for message to get the message computed from the exception by DefaultMessage.
                    remoteException = new DispatchException(
                        message: null,
                        errorCode,
                        _includeInnerExceptionDetails ? exception : null);
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
                            _includeInnerExceptionDetails ? encodeException : null));
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
                    SliceEncodeOptions encodeOptions = request.Features.Get<ISliceFeature>()?.EncodeOptions ??
                        SliceEncodeOptions.Default;

                    var pipe = new Pipe(encodeOptions.PipeOptions);

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

                lock (_mutex)
                {
                    if (--_dispatchCount == 0 && _isReadOnly)
                    {
                        _dispatchesCompleted.TrySetResult();
                    }
                }

                _dispatchSemaphore?.Release();
            }

            if (request.IsOneway)
            {
                return;
            }

            try
            {
                EncodeHeader();

                // SendPayloadAsync takes care of the completion of the response payload, payload stream and stream
                // output.
                await SendPayloadAsync(response, stream, dispatchCts.Token).ConfigureAwait(false);
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

    private void CheckRemoteHeaderSize(int headerSize)
    {
        if (headerSize > _maxRemoteHeaderSize)
        {
            throw new ProtocolException(
                $"header size ({headerSize}) is greater than the remote peer's max header size ({_maxRemoteHeaderSize})");
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
            // Ignore the reason of the reads/writes close.
        }

        lock (_mutex)
        {
            if (!stream.IsRemote)
            {
                _ = _localStreams.Remove(stream);
                if (_localStreams.Count == 0 && _isReadOnly)
                {
                    _localStreamsCompleted.TrySetResult();
                }
            }

            if (--_streamCount == 0 && !_isReadOnly)
            {
                EnableIdleCheck();
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
