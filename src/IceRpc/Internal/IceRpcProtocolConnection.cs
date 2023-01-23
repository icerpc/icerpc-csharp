// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Security.Authentication;

namespace IceRpc.Internal;

internal sealed class IceRpcProtocolConnection : IProtocolConnection
{
    public Task<Exception?> Closed => _closedTcs.Task;

    public ServerAddress ServerAddress => _transportConnection.ServerAddress;

    public Task ShutdownRequested => _shutdownRequestedTcs.Task;

    private const int MaxGoAwayFrameBodySize = 16;
    private const int MaxSettingsFrameBodySize = 1024;

    private bool IsServer => _transportConnectionInformation is not null;

    private readonly CancellationTokenSource _acceptRequestsCts = new();

    private Task? _acceptRequestsTask;

    private readonly TaskCompletionSource<Exception?> _closedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task<TransportConnectionInformation>? _connectTask;
    private IConnectionContext? _connectionContext; // non-null once the connection is established
    private IMultiplexedStream? _controlStream;
    private int _dispatchCount;
    private readonly IDispatcher? _dispatcher;
    private readonly TaskCompletionSource _dispatchesCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly SemaphoreSlim? _dispatchSemaphore;

    // This cancellation token source is canceled when the connection is disposed.
    private readonly CancellationTokenSource _disposedCts = new();

    private Task? _disposeTask;
    private readonly Action<Exception> _faultedTaskAction;

    // The number of bytes we need to encode a size up to _maxRemoteHeaderSize. It's 2 for DefaultMaxHeaderSize.
    private int _headerSizeLength = 2;

    private string? _invocationRefusedMessage;

    // The ID of the last bidirectional stream accepted by this connection. It's null as long as no bidirectional stream
    // was accepted.
    private ulong? _lastRemoteBidirectionalStreamId;

    // The ID of the last unidirectional stream accepted by this connection. It's null as long as no unidirectional
    // stream (other than _remoteControlStream) was accepted.
    private ulong? _lastRemoteUnidirectionalStreamId;
    private readonly int _maxLocalHeaderSize;
    private readonly object _mutex = new();
    private int _peerMaxHeaderSize = ConnectionOptions.DefaultMaxIceRpcHeaderSize;

    // Represents the streams of invocations where the corresponding request _may_ not have been received or dispatched
    // by the peer yet.
    private readonly Dictionary<IMultiplexedStream, CancellationTokenSource> _pendingInvocations = new();
    private Task<IceRpcGoAway>? _readGoAwayTask;

    // A connection refuses invocations when it's disposed, shut down, shutting down or merely "shutdown requested".
    private bool _refuseInvocations;

    private IMultiplexedStream? _remoteControlStream;

    // The thread that completes this TCS can run the continuations, and as a result its result must be set without
    // holding a lock on _mutex.
    private readonly TaskCompletionSource _shutdownRequestedTcs = new();

    private Task? _shutdownTask;

    private int _streamCount;
    private readonly TaskCompletionSource _streamsClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TimeSpan _timeout;
    private readonly Timer _timeoutTimer;

    private readonly IMultiplexedConnection _transportConnection;

    // Only set for server connections.
    private readonly TransportConnectionInformation? _transportConnectionInformation;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(IceRpcProtocolConnection)}");
            }
            if (_connectTask is not null)
            {
                throw new InvalidOperationException("Cannot call connect more than once.");
            }

            _connectTask = PerformConnectAsync();
        }
        return _connectTask;

        async Task<TransportConnectionInformation> PerformConnectAsync()
        {
            // Make sure we execute the function without holding the connection mutex lock.
            await Task.Yield();

            // _disposedCts is not disposed at this point because DisposeAsync waits for the completion of _connectTask.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _disposedCts.Token);

            TransportConnectionInformation transportConnectionInformation;

            try
            {
                // If the transport connection information is null, we need to connect the transport connection. It's
                // null for client connections. The transport connection of a server connection is established by
                // Server.
                transportConnectionInformation = _transportConnectionInformation ??
                    await _transportConnection.ConnectAsync(connectCts.Token).ConfigureAwait(false);

                _controlStream = await _transportConnection.CreateStreamAsync(
                    false,
                    connectCts.Token).ConfigureAwait(false);

                var settings = new IceRpcSettings(
                    _maxLocalHeaderSize == ConnectionOptions.DefaultMaxIceRpcHeaderSize ?
                        ImmutableDictionary<IceRpcSettingKey, ulong>.Empty :
                        new Dictionary<IceRpcSettingKey, ulong>
                        {
                            [IceRpcSettingKey.MaxHeaderSize] = (ulong)_maxLocalHeaderSize
                        });

                try
                {
                    await SendControlFrameAsync(
                        IceRpcControlFrameType.Settings,
                        settings.Encode,
                        connectCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // If we fail to send the Settings frame, we are in an abortive closure and we close Output to allow
                    // the peer to continue if it's waiting for us. This could happen when the cancellation token is
                    // canceled.
                    _controlStream!.Output.CompleteOutput(success: false);
                    throw;
                }

                // Wait for the remote control stream to be accepted and read the protocol Settings frame
                _remoteControlStream = await _transportConnection.AcceptStreamAsync(
                    connectCts.Token).ConfigureAwait(false);

                await ReceiveControlFrameHeaderAsync(
                    IceRpcControlFrameType.Settings,
                    connectCts.Token).ConfigureAwait(false);

                await ReceiveSettingsFrameBody(connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_disposedCts.Token.IsCancellationRequested)
            {
                throw new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The connection establishment was aborted because the connection was disposed.");
            }
            catch (OperationCanceledException)
            {
                Debug.Assert(cancellationToken.IsCancellationRequested);
                var exception = new OperationCanceledException(cancellationToken);
                TryCompleteClosed(exception, "The connection establishment was canceled.");
                throw exception;
            }
            catch (IceRpcException exception)
            {
                TryCompleteClosed(exception, "The connection establishment failed.");
                throw;
            }
            catch (InvalidDataException exception)
            {
                TryCompleteClosed(exception, "The connection establishment failed.");
                throw;
            }
            catch (AuthenticationException exception)
            {
                TryCompleteClosed(exception, "The connection establishment failed.");
                throw;
            }
            catch (Exception exception)
            {
                Debug.Fail($"ConnectAsync failed with an unexpected exception: {exception}");
                TryCompleteClosed(exception, "The connection establishment failed.");
                throw;
            }

            // This needs to be set before starting the accept requests task below.
            _connectionContext = new ConnectionContext(this, transportConnectionInformation);

            // We assign _readGoAwayTask and _acceptRequestsTask with _mutex locked to make sure this assignment
            // occurs before the start of DisposeAsync. Once _disposeTask is not null, _readGoAwayTask etc are
            // immutable.
            lock (_mutex)
            {
                if (_disposeTask is not null)
                {
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The connection establishment was aborted because the connection was disposed.");
                }

                // Start a task to read the go away frame from the control stream and initiate shutdown.
                _readGoAwayTask = ReadGoAwayAsync(_disposedCts.Token);

                // Start a task that accepts requests (the "accept requests loop")
                _acceptRequestsTask = AcceptRequestsAsync(_acceptRequestsCts.Token);
            }

            return transportConnectionInformation;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            if (_disposeTask is null)
            {
                RefuseNewInvocations("The connection was disposed.");

                if (_streamCount == 0)
                {
                    _streamsClosed.TrySetResult();
                }
                if (_dispatchCount == 0)
                {
                    _dispatchesCompleted.TrySetResult();
                }

                _shutdownTask ??= Task.CompletedTask;
                _disposeTask = PerformDisposeAsync();
            }
        }
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            // Make sure we execute the code below without holding the mutex lock.
            await Task.Yield();

            _disposedCts.Cancel();
            _acceptRequestsCts.Cancel();

            // We don't lock _mutex since once _disposeTask is not null, _connectTask etc are immutable.

            if (_connectTask is null)
            {
                _ = _closedTcs.TrySetResult(null); // disposing non-connected connection
            }
            else
            {
                // Since we await _dispatchesCompleted.Task before disposing the transport connection (or disposing
                // anything else), a dispatch can't get an IceRpcException(OperationAborted) when sending a response.
                try
                {
                    await Task.WhenAll(
                        _connectTask,
                        _acceptRequestsTask ?? Task.CompletedTask,
                        _readGoAwayTask ?? Task.CompletedTask,
                        _shutdownTask,
                        _dispatchesCompleted.Task).ConfigureAwait(false);
                }
                catch
                {
                    // Expected if any of these tasks failed or was canceled. Each task takes care of handling
                    // unexpected exceptions so there's no need to handle them here.
                }

                // We set the result after awaiting _shutdownTask, in case _shutdownTask was still running and about to
                // complete successfully.
                _ = _closedTcs.TrySetResult(new ObjectDisposedException($"{typeof(IceRpcProtocolConnection)}"));
            }

            // We didn't wait for _streams.Closed above. Invocations and the sending of payload continuation that are
            // not canceled yet can be aborted by this disposal.
            await _transportConnection.DisposeAsync().ConfigureAwait(false);

            // It's safe to complete the output since write operations have been completed by the transport connection
            // disposal.
            _controlStream?.Output.Complete();

            // It's safe to complete the input since read operations have been completed by the transport connection
            // disposal.
            _remoteControlStream?.Input.Complete();

            foreach (CancellationTokenSource invocationCts in _pendingInvocations.Values)
            {
                invocationCts.Dispose();
            }

            _dispatchSemaphore?.Dispose();
            _disposedCts.Dispose();
            _acceptRequestsCts.Dispose();

            await _timeoutTimer.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Protocol != ServerAddress.Protocol)
        {
            throw new InvalidOperationException(
                $"Cannot send {request.Protocol} request on {ServerAddress.Protocol} connection.");
        }

        CancellationToken disposedCancellationToken;
        CancellationToken acceptRequestsCancellationToken;
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(IceRpcProtocolConnection)}");
            }
            if (_refuseInvocations)
            {
                throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage);
            }
            if (_connectTask is null)
            {
                throw new InvalidOperationException("Cannot invoke on a connection before connecting it.");
            }
            if (!IsServer && !_connectTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "Cannot invoke on a client connection that is not fully established.");
            }
            // It's possible but rare to invoke on a server connection that is still connecting.

            if (request.ServiceAddress.Fragment.Length > 0)
            {
                throw new NotSupportedException("The icerpc protocol does not support fragments.");
            }

            disposedCancellationToken = _disposedCts.Token;
            acceptRequestsCancellationToken = _acceptRequestsCts.Token;
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            var invocationCts = CancellationTokenSource.CreateLinkedTokenSource(disposedCancellationToken);
            CancellationToken invocationCancellationToken = invocationCts.Token;

            // We unregister this cancellationToken when this async method completes (it completes successfully when we
            // receive a response (for twoway) or the request Payload is sent (oneway)).
            // This way, the sending of the payload continuation can continue in the background after this async method
            // completes independently of cancellationToken.
            using CancellationTokenRegistration tokenRegistration = cancellationToken.UnsafeRegister(
                cts => ((CancellationTokenSource)cts!).Cancel(),
                invocationCts);

            IMultiplexedStream? stream = null;
            PipeReader? streamInput = null;
            try
            {
                // Create the stream.
                try
                {
                    // We also want to cancel CreateStreamAsync as soon as the connection is being shutdown instead of
                    // waiting for its disposal.
                    using CancellationTokenRegistration _ = acceptRequestsCancellationToken.UnsafeRegister(
                        cts => ((CancellationTokenSource)cts!).Cancel(),
                        invocationCts);

                    stream = await _transportConnection.CreateStreamAsync(
                        bidirectional: !request.IsOneway,
                        invocationCancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Connection was shutdown or disposed.
                    lock (_mutex)
                    {
                        Debug.Assert(_refuseInvocations);
                        throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage);
                    }
                }
                catch (IceRpcException exception)
                {
                    TryCompleteClosed(exception, "The connection was lost.");
                    throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage);
                }
                catch (Exception exception)
                {
                    Debug.Assert(false, $"InvokeAsync failed with an unexpected exception: {exception}");
                    TryCompleteClosed(exception, "The connection was lost.");
                    throw;
                }

                streamInput = stream.IsBidirectional ? stream.Input : null;

                lock (_mutex)
                {
                    if (_refuseInvocations)
                    {
                        throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage);
                    }

                    if (++_streamCount == 1)
                    {
                        DisableIdleCheck();
                    }

                    // Keep track of the invocation cancellation token source for the shutdown logic.
                    _pendingInvocations.Add(stream, invocationCts);
                    invocationCts = null; // invocationCts is disposed by UnregisterOnReadsAndWritesClosedAsync

                    _ = UnregisterOnReadsAndWritesClosedAsync(stream);
                }
            }
            catch
            {
                stream?.Output.CompleteOutput(success: false);
                streamInput?.Complete();
                throw;
            }
            finally
            {
                invocationCts?.Dispose();
            }

            try
            {
                try
                {
                    EncodeHeader(stream.Output);
                }
                catch
                {
                    stream.Output.CompleteOutput(success: false);
                    throw;
                }

                // SendPayloadAsync takes ownership of stream.Output
                await SendPayloadAsync(request, stream.Output, stream.WritesClosed, invocationCancellationToken)
                    .ConfigureAwait(false);

                if (request.IsOneway)
                {
                    return new IncomingResponse(request, _connectionContext!);
                }

                Debug.Assert(streamInput is not null);

                ReadResult readResult = await streamInput.ReadSegmentAsync(
                    SliceEncoding.Slice2,
                    _maxLocalHeaderSize,
                    invocationCancellationToken).ConfigureAwait(false);

                // Nothing cancels the stream input pipe reader.
                Debug.Assert(!readResult.IsCanceled);

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException("Received an icerpc response with an empty header.");
                }

                (StatusCode statusCode, string? errorMessage, IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields, PipeReader? fieldsPipeReader) =
                    DecodeHeader(readResult.Buffer);
                stream.Input.AdvanceTo(readResult.Buffer.End);

                var response = new IncomingResponse(
                    request,
                    _connectionContext!,
                    statusCode,
                    errorMessage,
                    fields,
                    fieldsPipeReader)
                {
                    Payload = streamInput
                };

                streamInput = null; // response now owns the stream input
                return response;
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (disposedCancellationToken.IsCancellationRequested)
                {
                    // DisposeAsync aborted request.
                    throw new IceRpcException(IceRpcError.OperationAborted);
                }
                else
                {
                    throw new IceRpcException(IceRpcError.InvocationCanceled, "The connection is shutting down.");
                }
            }
            finally
            {
                streamInput?.Complete();
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
                CheckPeerHeaderSize(headerSize);
                SliceEncoder.EncodeVarUInt62((uint)headerSize, sizePlaceholder);
            }

            static (StatusCode StatusCode, string? ErrorMessage, IDictionary<ResponseFieldKey, ReadOnlySequence<byte>>, PipeReader?) DecodeHeader(
                ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

                StatusCode statusCode = decoder.DecodeStatusCode();
                string? errorMessage = statusCode == StatusCode.Success ? null : decoder.DecodeString();

                (IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields, PipeReader? pipeReader) =
                    DecodeFieldDictionary(ref decoder, (ref SliceDecoder decoder) => decoder.DecodeResponseFieldKey());

                return (statusCode, errorMessage, fields, pipeReader);
            }
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(IceRpcProtocolConnection)}");
            }
            if (_shutdownTask is not null)
            {
                throw new InvalidOperationException("Cannot call ShutdownAsync more than once.");
            }
            if (_connectTask is null || !_connectTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Cannot shut down a protocol connection before it's connected.");
            }

            RefuseNewInvocations("The connection was shut down.");

            if (_streamCount == 0)
            {
                _streamsClosed.TrySetResult();
            }
            if (_dispatchCount == 0)
            {
                _dispatchesCompleted.TrySetResult();
            }

            _shutdownTask = PerformShutdownAsync();
        }

        _acceptRequestsCts.Cancel();
        return _shutdownTask;

        async Task PerformShutdownAsync()
        {
            await Task.Yield(); // exit mutex lock

            try
            {
                Debug.Assert(_acceptRequestsTask is not null);
                await _acceptRequestsTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                // Once _isShutdown is true, _lastRemoteBidirectionalStreamId and _lastRemoteUnidirectionalStreamId are
                // immutable.

                // Since DisposeAsync waits for _shutdownTask completion, _disposedCts is not disposed at this point.
                using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _disposedCts.Token);

                // When this peer is the server endpoint, the first accepted bidirectional stream ID is 0. When this
                // peer is the client endpoint, the first accepted bidirectional stream ID is 1.
                IceRpcGoAway goAwayFrame = new(
                     _lastRemoteBidirectionalStreamId is ulong value ? value + 4 : (IsServer ? 0ul : 1ul),
                     (_lastRemoteUnidirectionalStreamId ?? _remoteControlStream!.Id) + 4);

                try
                {
                    await SendControlFrameAsync(
                        IceRpcControlFrameType.GoAway,
                        goAwayFrame.Encode,
                        shutdownCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // If we fail to send the GoAway frame, we are in an abortive closure and we close Output to allow
                    // the peer to continue if it's waiting for us. This could happen when the cancellation token is
                    // canceled.
                    _controlStream!.Output.CompleteOutput(success: false);
                    throw;
                }

                try
                {
                    // Wait for the peer to send back a GoAway frame. The task should already be completed if the
                    // shutdown was initiated by the peer.
                    IceRpcGoAway peerGoAwayFrame = await _readGoAwayTask!.WaitAsync(shutdownCts.Token).ConfigureAwait(false);

                    // Abort streams for outgoing requests that were not dispatched by the peer. The invocations will
                    // throw IceRpcException(InvocationCanceled) which can be retried. Since _isShutdown is true,
                    // _pendingInvocations is immutable at this point.
                    foreach ((IMultiplexedStream stream, CancellationTokenSource invocationCts) in _pendingInvocations)
                    {
                        if (!stream.IsStarted ||
                            stream.Id >= (stream.IsBidirectional ?
                                peerGoAwayFrame.BidirectionalStreamId : peerGoAwayFrame.UnidirectionalStreamId))
                        {
                            invocationCts.Cancel();
                        }
                    }

                    // Wait for network activity on streams (other than control streams) to cease.
                    await _streamsClosed.Task.WaitAsync(shutdownCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    // Close the control stream to notify the peer that on our side, all the streams completed and that
                    // it can close the transport connection whenever it likes.
                    // We also do this if an exception is thrown (such as OperationCanceledException): we're now in an
                    // abortive closure and from our point of view, it's ok for the peer to close the transport
                    // connection. We don't close the transport connection immediately as this would kill the streams in
                    // the peer and we want to give the peer a chance to complete its shutdown gracefully.
                    _controlStream!.Output.Complete();
                }

                // Wait for the peer notification that on its side all the streams are completed. It's important to wait
                // for this notification before closing the connection. In particular with Quic where closing the
                // connection before all the streams are processed could lead to a stream failure.
                try
                {
                    // Wait for the _remoteControlStream Input completion.
                    ReadResult readResult = await _remoteControlStream!.Input.ReadAsync(shutdownCts.Token)
                        .ConfigureAwait(false);

                    Debug.Assert(!readResult.IsCanceled);

                    if (!readResult.IsCompleted || !readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException(
                            "Received bytes on the control stream after receiving the GoAway frame.");
                    }

                    // We can now safely close the connection.
                    await _transportConnection.CloseAsync(
                        MultiplexedConnectionCloseError.NoError,
                        shutdownCts.Token).ConfigureAwait(false);
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.ConnectionClosedByPeer)
                {
                    // Expected if the peer closed the connection first.
                }

                // We wait for the completion of the dispatches that we created.
                await _dispatchesCompleted.Task.WaitAsync(shutdownCts.Token).ConfigureAwait(false);

                _closedTcs.SetResult(null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var exception = new OperationCanceledException(cancellationToken);
                TryCompleteClosed(exception, "The connection shutdown was canceled.");
                throw exception;
            }
            catch (OperationCanceledException)
            {
                Debug.Assert(_disposedCts.Token.IsCancellationRequested);
                throw new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The connection shutdown was aborted because the connection was disposed.");
            }
            catch (IceRpcException exception)
            {
                TryCompleteClosed(exception, "The connection shutdown failed.");
                throw;
            }
            catch (InvalidDataException exception)
            {
                TryCompleteClosed(exception, "The connection shutdown failed.");
                throw;
            }
            catch (Exception exception)
            {
                Debug.Fail($"ShutdownAsync failed with an unexpected exception: {exception}");
                TryCompleteClosed(exception, "The connection shutdown failed.");
                throw;
            }
        }
    }

    internal IceRpcProtocolConnection(
        IMultiplexedConnection transportConnection,
        TransportConnectionInformation? transportConnectionInformation,
        ConnectionOptions options)
    {
        _transportConnection = transportConnection;
        _dispatcher = options.Dispatcher;
        _faultedTaskAction = options.FaultedTaskAction;
        _maxLocalHeaderSize = options.MaxIceRpcHeaderSize;
        _transportConnectionInformation = transportConnectionInformation;

        if (options.MaxDispatches > 0)
        {
            _dispatchSemaphore = new SemaphoreSlim(
                initialCount: options.MaxDispatches,
                maxCount: options.MaxDispatches);
        }

        _timeout = options.Timeout;
        _timeoutTimer = new Timer(_ =>
        {
            bool requestShutdown = false;

            lock (_mutex)
            {
                if (_dispatchCount == 0 && _streamCount == 0 && _shutdownTask is null)
                {
                    requestShutdown = true;
                    RefuseNewInvocations(
                        $"The connection was shut down because it was idle for over {_timeout.TotalSeconds} s.");
                }
            }

            if (requestShutdown)
            {
                // TrySetResult must be called outside the mutex lock
                _shutdownRequestedTcs.TrySetResult();
            }
        });
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

    private async Task AcceptRequestsAsync(CancellationToken cancellationToken)
    {
        await Task.Yield(); // exit mutex lock

        // Wait for _connectTask (which spawned the task running this method) to complete. This way, we won't dispatch
        // any request until _connectTask has completed successfully, and indirectly we won't make any invocation until
        // _connectTask has completed successfully. The creation of the _acceptRequestsTask is the last action taken by
        // _connectTask and as a result this await can't fail.
        _ = await _connectTask!.ConfigureAwait(false);

        // The idle check requests shutdown and we want to make sure we only request it after _connectTask completed
        // successfully.
        EnableIdleCheck();

        try
        {
            // We check the cancellation token for each iteration because we want to exit the accept requests
            // loop as soon as ShutdownAsync requests this cancellation, even when more streams can be accepted
            // without waiting.
            while (!cancellationToken.IsCancellationRequested)
            {
                // When _dispatcher is null, the multiplexed connection MaxUnidirectionalStreams and
                // MaxBidirectionalStreams options are configured to not accept any request-stream from the
                // peer. As a result, when _dispatcher is null, this call will block indefinitely until the
                // transport connection is closed or disposed, or until the cancellation token is canceled
                // by ShutdownAsync.
                IMultiplexedStream stream = await _transportConnection.AcceptStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                lock (_mutex)
                {
                    // We don't want to increment _dispatchCount or _streamCount when the connection is
                    // shutting down or being disposed.
                    if (_shutdownTask is not null)
                    {
                        // Note that cancellationToken may not be canceled yet at this point.
                        throw new OperationCanceledException();
                    }

                    // The multiplexed connection guarantees that the IDs of accepted streams of a given
                    // type have ever increasing values.

                    if (stream.IsBidirectional)
                    {
                        _lastRemoteBidirectionalStreamId = stream.Id;
                    }
                    else
                    {
                        _lastRemoteUnidirectionalStreamId = stream.Id;
                    }

                    ++_dispatchCount;

                    if (++_streamCount == 1)
                    {
                        // We were idle, we no longer are.
                        DisableIdleCheck();
                    }

                    _ = UnregisterOnReadsAndWritesClosedAsync(stream);

                    // Start a task to read the stream and dispatch the request. We pass CancellationToken.None
                    // to Task.Run because DispatchRequestAsync must clean-up the stream.
                    CancellationToken disposedCancellationToken = _disposedCts.Token;
                    _ = Task.Run(
                        () => DispatchRequestAsync(stream, disposedCancellationToken),
                        CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected, the associated cancellation token source was canceled.
        }
        catch (IceRpcException exception)
        {
            TryCompleteClosed(exception, "The connection was lost.");
            throw;
        }
        catch (Exception exception)
        {
            Debug.Fail($"The accept stream task failed with an unexpected exception: {exception}");
            TryCompleteClosed(exception, "The connection was lost.");
            throw;
        }
    }

    private void CheckPeerHeaderSize(int headerSize)
    {
        if (headerSize > _peerMaxHeaderSize)
        {
            throw new IceRpcException(
                IceRpcError.LimitExceeded,
                $"The header size ({headerSize}) for an icerpc request or response is greater than the peer's max header size ({_peerMaxHeaderSize}).");
        }
    }

    private void DisableIdleCheck() => _timeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private async Task DispatchRequestAsync(IMultiplexedStream stream, CancellationToken cancellationToken)
    {
        using var dispatchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (stream.IsBidirectional)
        {
            // If the peer is no longer interested in the response of the dispatch, we cancel the dispatch.
            _ = CancelDispatchOnWritesClosedAsync();

            async Task CancelDispatchOnWritesClosedAsync()
            {
                await stream.WritesClosed.ConfigureAwait(false);

                try
                {
                    dispatchCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Expected if already disposed by 'var'.
                }
            }
        }

        PipeReader? fieldsPipeReader = null;
        PipeReader? streamInput = stream.Input;
        PipeWriter? streamOutput = stream.IsBidirectional ? stream.Output : null;
        IncomingRequest? request = null;
        bool success = false;
        try
        {
            ReadResult readResult = await streamInput.ReadSegmentAsync(
                SliceEncoding.Slice2,
                _maxLocalHeaderSize,
                cancellationToken).ConfigureAwait(false);

            if (readResult.Buffer.IsEmpty)
            {
                throw new InvalidDataException("Received icerpc request with empty header.");
            }

            (IceRpcRequestHeader header, IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields, fieldsPipeReader) =
                DecodeHeader(readResult.Buffer);
            streamInput.AdvanceTo(readResult.Buffer.End);

            request = new IncomingRequest(_connectionContext!)
            {
                Fields = fields,
                IsOneway = !stream.IsBidirectional,
                Operation = header.Operation,
                Path = header.Path,
                Payload = streamInput
            };

            streamInput = null; // the request now owns streamInput

            await PerformDispatchRequestAsync(request, dispatchCts.Token).ConfigureAwait(false);
            success = true;
        }
        catch (IceRpcException exception) when (
            exception.IceRpcError is IceRpcError.ConnectionAborted or IceRpcError.TruncatedData)
        {
            // ConnectionAborted is expected when the peer aborts the connection.
            // TruncatedData is expected when reading a request header. It can also be
            // thrown when reading a response payload tied to an incoming IceRPC payload.
        }
        catch (InvalidDataException)
        {
            // This occurs when we can't decode the request header or encode the response header.
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == dispatchCts.Token)
        {
            // expected, dispatch canceled by peer or dispose
        }
        catch (Exception exception)
        {
            // not expected
            _faultedTaskAction(exception);
        }
        finally
        {
            if (!success)
            {
                // We always need to complete streamOutput when an exception is thrown. For example, we received an
                // invalid request header that we could not decode.
                streamOutput?.CompleteOutput(success: false);
                streamInput?.Complete();
            }
            fieldsPipeReader?.Complete();
            request?.Dispose();

            lock (_mutex)
            {
                if (--_dispatchCount == 0 && _shutdownTask is not null)
                {
                    _ = _dispatchesCompleted.TrySetResult();
                }
            }
        }

        async Task PerformDispatchRequestAsync(IncomingRequest request, CancellationToken cancellationToken)
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
                    throw new InvalidOperationException(
                        "The dispatcher did not return the last response created for this request.");
                }
            }
            catch when (request.IsOneway)
            {
                // No reply for oneway requests or if the connection is disposed.
                return;
            }
            catch (OperationCanceledException exception) when (cancellationToken == exception.CancellationToken)
            {
                throw;
            }
            catch (Exception exception)
            {
                // We convert any exception into a dispatch exception if it's not already one.
                if (exception is not DispatchException dispatchException || dispatchException.ConvertToUnhandled)
                {
                    StatusCode statusCode = exception switch
                    {
                        InvalidDataException => StatusCode.InvalidData,
                        IceRpcException iceRpcException when iceRpcException.IceRpcError == IceRpcError.TruncatedData =>
                             StatusCode.TruncatedPayload,
                        _ => StatusCode.UnhandledException
                    };

                    // We want the default error message for this new exception.
                    dispatchException = new DispatchException(statusCode, message: null, exception);
                }

                // The payload of response below is always empty.
                response = new OutgoingResponse(request, dispatchException);
            }

            if (request.IsOneway)
            {
                return;
            }

            Debug.Assert(streamOutput is not null);

            EncodeHeader();

            // SendPayloadAsync takes ownership of streamOutput
            await SendPayloadAsync(response, streamOutput, stream.WritesClosed, cancellationToken)
                .ConfigureAwait(false);

            void EncodeHeader()
            {
                var encoder = new SliceEncoder(stream.Output, SliceEncoding.Slice2);

                // Write the IceRpc response header.
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(_headerSizeLength);
                int headerStartPos = encoder.EncodedByteCount;

                encoder.EncodeStatusCode(response.StatusCode);
                if (response.StatusCode > StatusCode.Success)
                {
                    encoder.EncodeString(response.ErrorMessage!);
                }

                encoder.EncodeDictionary(
                    response.Fields,
                    (ref SliceEncoder encoder, ResponseFieldKey key) => encoder.EncodeResponseFieldKey(key),
                    (ref SliceEncoder encoder, OutgoingFieldValue value) =>
                        value.Encode(ref encoder, _headerSizeLength));

                // We're done with the header encoding, write the header size.
                int headerSize = encoder.EncodedByteCount - headerStartPos;
                CheckPeerHeaderSize(headerSize);
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

    // The idle check executes once in _timeout. By then either:
    // - the connection is no longer idle (and DisableIdleCheck was called or is being called)
    // - the connection is still idle and we request shutdown
    private void EnableIdleCheck() => _timeoutTimer.Change(_timeout, Timeout.InfiniteTimeSpan);

    private async Task<IceRpcGoAway> ReadGoAwayAsync(CancellationToken cancellationToken)
    {
        await Task.Yield(); // exit mutex lock

        // Wait for _connectTask (which spawned the task running this method) to complete. This await can't fail.
        // This guarantees this method won't request a shutdown until after _connectTask completed successfully.
        _ = await _connectTask!.ConfigureAwait(false);

        PipeReader remoteInput = _remoteControlStream!.Input!;

        try
        {
            // Wait to receive the GoAway frame.
            await ReceiveControlFrameHeaderAsync(IceRpcControlFrameType.GoAway, cancellationToken)
                .ConfigureAwait(false);

            ReadResult readResult = await remoteInput.ReadSegmentAsync(
                SliceEncoding.Slice2,
                MaxGoAwayFrameBodySize,
                cancellationToken).ConfigureAwait(false);

            // We don't call CancelPendingRead on remoteInput
            Debug.Assert(!readResult.IsCanceled);

            IceRpcGoAway goAwayFrame;
            try
            {
                goAwayFrame = SliceEncoding.Slice2.DecodeBuffer(
                    readResult.Buffer,
                    (ref SliceDecoder decoder) => new IceRpcGoAway(ref decoder));
            }
            finally
            {
                remoteInput.AdvanceTo(readResult.Buffer.End);
            }

            RefuseNewInvocations(
                "The connection was shut down because it received a GoAway frame from the peer.");
            _shutdownRequestedTcs.TrySetResult();

            return goAwayFrame;
        }
        catch (OperationCanceledException)
        {
            // The connection is disposed and we let this exception cancel the task.
            throw;
        }
        catch (Exception exception)
        {
            Debug.Assert(
                exception is IceRpcException or InvalidDataException,
                $"The read go away task failed with an unexpected exception: {exception}");

            TryCompleteClosed(exception, "The connection was lost.");
            throw;
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
                throw new InvalidDataException("Received an invalid empty control frame.");
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
                       $"Received frame type {frameType} but expected {expectedFrameType}.");
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

    private async ValueTask ReceiveSettingsFrameBody(CancellationToken cancellationToken)
    {
        // We are still in the single-threaded initialization at this point.

        PipeReader input = _remoteControlStream!.Input;
        ReadResult readResult = await input.ReadSegmentAsync(
            SliceEncoding.Slice2,
            MaxSettingsFrameBodySize,
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
                _peerMaxHeaderSize = ConnectionOptions.IceRpcCheckMaxHeaderSize((long)value);
                _headerSizeLength = SliceEncoder.GetVarUInt62EncodedSize(value);
            }
            // all other settings are unknown and ignored
        }
        finally
        {
            input.AdvanceTo(readResult.Buffer.End);
        }
    }

    private void RefuseNewInvocations(string message)
    {
        lock (_mutex)
        {
            _refuseInvocations = true;
            _invocationRefusedMessage ??= message;
        }
    }

    /// <summary>Sends the payload and payload continuation of an outgoing frame, and takes ownership of streamOutput.
    /// </summary>
    private async ValueTask SendPayloadAsync(
        OutgoingFrame outgoingFrame,
        PipeWriter streamOutput,
        Task streamWritesClosed,
        CancellationToken cancellationToken)
    {
        try
        {
            streamOutput = outgoingFrame.GetPayloadWriter(streamOutput);

            FlushResult flushResult = await CopyReaderToWriterAsync(
                outgoingFrame.Payload,
                streamOutput,
                endStream: outgoingFrame.PayloadContinuation is null,
                cancellationToken).ConfigureAwait(false);

            if (flushResult.IsCompleted)
            {
                // The remote reader doesn't want more data, we're done.
                streamOutput.CompleteOutput(success: true);
                outgoingFrame.Payload.Complete();
                outgoingFrame.PayloadContinuation?.Complete();
                return;
            }
            else if (flushResult.IsCanceled)
            {
                throw new InvalidOperationException(
                    "A payload writer interceptor is not allowed to return a canceled flush result.");
            }
        }
        catch
        {
            streamOutput.CompleteOutput(success: false);
            throw;
        }

        outgoingFrame.Payload.Complete(); // the payload was sent successfully

        if (outgoingFrame.PayloadContinuation is null)
        {
            streamOutput.CompleteOutput(success: true);
        }
        else
        {
            // Send payloadContinuation in the background.
            PipeReader payloadContinuation = outgoingFrame.PayloadContinuation;
            outgoingFrame.PayloadContinuation = null; // we're now responsible for payloadContinuation

            _ = Task.Run(
                async () =>
                {
                    bool success = false;
                    try
                    {
                        // When we send an outgoing request, the cancellation token is invocationCts.Token. The
                        // cancellation of the token given to InvokeAsync/InvokeAsyncCore cancels invocationCts only
                        // until InvokeAsyncCore completes (see tokenRegistration); after that, the cancellation of this
                        // token has no effect on invocationCts, so it doesn't cancel the copying of
                        // payloadContinuation.
                        FlushResult flushResult = await CopyReaderToWriterAsync(
                            payloadContinuation,
                            streamOutput,
                            endStream: true,
                            cancellationToken).ConfigureAwait(false);

                        if (flushResult.IsCanceled)
                        {
                            throw new InvalidOperationException(
                                "A payload writer interceptor is not allowed to return a canceled flush result.");
                        }
                        success = true;
                    }
                    catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
                    {
                        // Expected when the connection is shut down.
                    }
                    catch (IceRpcException exception) when (
                        exception.IceRpcError is
                            IceRpcError.ConnectionAborted or
                            IceRpcError.OperationAborted or
                            IceRpcError.TruncatedData)
                    {
                        // ConnectionAborted is expected when the peer aborts the connection.
                        // OperationAborted is expected when the local application disposes the connection.
                        // TruncatedData is expected when the payloadContinuation comes from an incoming IceRPC payload
                        // and the peer's Output is completed with an exception.
                    }
                    catch (Exception exception)
                    {
                        _faultedTaskAction(exception);
                    }
                    finally
                    {
                        streamOutput.CompleteOutput(success);
                        payloadContinuation.Complete();
                    }
                },
                CancellationToken.None); // we need this task to run to complete streamOutput and payloadContinuation
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
                    catch (OperationCanceledException exception) when (exception.CancellationToken == readCts.Token)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Debug.Assert(streamWritesClosed.IsCompleted);

                        // This either throws the WritesClosed exception or returns a completed FlushResult.
                        return await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    // we let other exceptions thrown by ReadAsync (including possibly an OperationCanceledException
                    // thrown incorrectly) escape.

                    if (readResult.IsCanceled)
                    {
                        // The application (or an interceptor/middleware) called CancelPendingRead on reader.
                        reader.AdvanceTo(readResult.Buffer.Start); // Did not consume any byte in reader.

                        writer.CompleteOutput(success: false); // we didn't copy everything
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
                    await streamWritesClosed.WaitAsync(readCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore the reason of the writes close, or the OperationCanceledException
                }

                readCts.Cancel();
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
            int frameSize = encoder.EncodedByteCount - startPos;
            SliceEncoder.EncodeVarUInt62((uint)frameSize, sizePlaceholder);
        }
    }

    private void TryCompleteClosed(Exception exception, string invocationRefusedMessage)
    {
        if (_closedTcs.TrySetResult(exception))
        {
            RefuseNewInvocations(invocationRefusedMessage);

            // Cancel pending AcceptStreamAsync or CreateStreamAsync calls.
            _acceptRequestsCts.Cancel();
        }
    }

    private async Task UnregisterOnReadsAndWritesClosedAsync(IMultiplexedStream stream)
    {
        // Wait for the stream's reading and writing side to be closed to unregister the stream from the connection.
        await Task.WhenAll(stream.ReadsClosed, stream.WritesClosed).ConfigureAwait(false);

        lock (_mutex)
        {
            if (!stream.IsRemote && _shutdownTask is null)
            {
                if (_pendingInvocations.Remove(stream, out CancellationTokenSource? cts))
                {
                    cts.Dispose();
                }
                else
                {
                    Debug.Fail("Did not find multiplexed stream in pending invocations");
                }
            }

            if (--_streamCount == 0)
            {
                if (_shutdownTask is not null)
                {
                    _streamsClosed.TrySetResult();
                }
                else if (!_refuseInvocations)
                {
                    // We enable the idle check in order to complete ShutdownRequested when idle for too long.
                    // _refuseInvocations is true when the connection is either about to be "shutdown requested", or
                    // shut down / disposed, or aborted (with Closed completed). We don't need to complete
                    // ShutdownRequested in any of these situations.
                    EnableIdleCheck();
                }
            }
        }
    }
}
