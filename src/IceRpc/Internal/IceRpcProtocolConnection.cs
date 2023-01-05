// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal;

internal sealed class IceRpcProtocolConnection : IProtocolConnection
{
    public Task<Exception?> Closed => _closedTcs.Task;

    public ServerAddress ServerAddress => _transportConnection.ServerAddress;

    public Task ShutdownRequested => _shutdownRequestedTcs.Task;

    private const int MaxGoAwayFrameBodySize = 16;
    private const int MaxSettingsFrameBodySize = 1024;

    private Task? _acceptRequestsTask;
    private readonly CancellationTokenSource _acceptStreamCts = new();
    private readonly TaskCompletionSource<Exception?> _closedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task<TransportConnectionInformation>? _connectTask;
    private IConnectionContext? _connectionContext; // non-null once the connection is established
    private IMultiplexedStream? _controlStream;
    private int _dispatchCount;
    private readonly IDispatcher? _dispatcher;
    private readonly CancellationTokenSource _dispatchesAndInvocationsCts = new();
    private readonly TaskCompletionSource _dispatchesCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly SemaphoreSlim? _dispatchSemaphore;
    private Task? _disposeTask;
    private readonly Action<Exception> _faultedTaskAction;

    // The number of bytes we need to encode a size up to _maxRemoteHeaderSize. It's 2 for DefaultMaxHeaderSize.
    private int _headerSizeLength = 2;

    private readonly TimeSpan _idleTimeout;
    private readonly Timer _idleTimeoutTimer;

    private readonly bool _isServer;
    private bool _isShutdown;

    // The ID of the last bidirectional stream accepted by this connection. It's null as long as no bidirectional stream
    // was accepted.
    private ulong? _lastRemoteBidirectionalStreamId;

    // The ID of the last unidirectional stream accepted by this connection. It's null as long as no unidirectional
    // stream (other than _remoteControlStream) was accepted.
    private ulong? _lastRemoteUnidirectionalStreamId;
    private readonly int _maxLocalHeaderSize;
    private readonly object _mutex = new();
    private string? _operationRefusedMessage;
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

    private int _streamCount;
    private readonly TaskCompletionSource _streamsClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IMultiplexedConnection _transportConnection;

    // Only set for server connections.
    private readonly TransportConnectionInformation? _transportConnectionInformation;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}");
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

            TransportConnectionInformation transportConnectionInformation;

            try
            {
                // If the transport connection information is null, we need to connect the transport connection. It's
                // null for client connections. The transport connection of a server connection is established by
                // Server.
                transportConnectionInformation = _transportConnectionInformation ??
                    await _transportConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);

                _controlStream = await _transportConnection.CreateStreamAsync(
                    false,
                    cancellationToken).ConfigureAwait(false);

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
            }
            catch (Exception exception)
            {
                _closedTcs.SetResult(exception);
                RefuseNewInvocations("The connection establishment failed.");
                throw;
            }

            // Start a task to read the go away frame from the control stream and initiate shutdown.
            _readGoAwayTask = Task.Run(
                async () =>
                {
                    try
                    {
                        // Wait to receive the GoAway frame.
                        await ReceiveControlFrameHeaderAsync(
                            IceRpcControlFrameType.GoAway,
                            CancellationToken.None).ConfigureAwait(false);

                        Debug.Assert(_remoteControlStream.Input is not null);
                        ReadResult readResult = await _remoteControlStream.Input.ReadSegmentAsync(
                            SliceEncoding.Slice2,
                            MaxGoAwayFrameBodySize,
                            CancellationToken.None).ConfigureAwait(false);

                        // We don't call CancelPendingRead on _remoteControlStream.Input
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
                            _remoteControlStream.Input.AdvanceTo(readResult.Buffer.End);
                        }

                        RefuseNewInvocations("The connection was shut down because it received a GoAway frame from the peer.");
                        _shutdownRequestedTcs.TrySetResult();

                        return goAwayFrame;
                    }
                    catch (IceRpcException exception)
                    {
                        await DisposeTransportAsync("The connection was lost.", exception).ConfigureAwait(false);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        await DisposeTransportAsync("The connection failed due to an unhandled exception.", exception)
                            .ConfigureAwait(false);
                        Debug.Fail($"The read go away task completed due to an unhandled exception: {exception}");
                        throw;
                    }
                },
                CancellationToken.None);

            // This needs to be set before starting the accept requests task below.
            _connectionContext = new ConnectionContext(this, transportConnectionInformation);

            // Start a task that accepts requests (the "accept requests loop")
            _acceptRequestsTask = Task.Run(
                async () =>
                {
                    try
                    {
                        // We check the cancellation token for each iteration because we want to exit the accept
                        // requests loop as soon as ShutdownAsync requests this cancellation, even when more streams can
                        // be accepted without waiting.
                        while (!_acceptStreamCts.Token.IsCancellationRequested)
                        {
                            // When _dispatcher is null, the multiplexed connection MaxUnidirectionalStreams and
                            // MaxBidirectionalStreams options are configured to not accept any request-stream from the
                            // peer. As a result, when _dispatcher is null, this call will block indefinitely until the
                            // transport connection is closed or disposed, or until the cancellation token is canceled
                            // by ShutdownAsync.
                            IMultiplexedStream stream =
                                await _transportConnection.AcceptStreamAsync(_acceptStreamCts.Token)
                                    .ConfigureAwait(false);

                            CancellationToken cancellationToken = default;
                            lock (_mutex)
                            {
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

                                _ = UnregisterOnInputAndOutputCompletedAsync(stream);
                            }

                            // Start a task to dispatch the request.
                            _ = Task.Run(
                                async () =>
                                {
                                    using var dispatchCts = CancellationTokenSource.CreateLinkedTokenSource(
                                        _dispatchesAndInvocationsCts.Token);
                                    cancellationToken = dispatchCts.Token;

                                    if (stream.IsBidirectional)
                                    {
                                        // If the peer is no longer interested in the response of the dispatch, we
                                        // cancel the dispatch.
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
                                                // Expected if already disposed.
                                            }
                                        }
                                    }

                                    try
                                    {
                                        await DispatchRequestAsync(stream, cancellationToken).ConfigureAwait(false);
                                    }
                                    catch (IceRpcException exception) when (
                                        exception.IceRpcError is
                                            IceRpcError.OperationAborted or
                                            IceRpcError.TruncatedData)
                                    {
                                        // OperationAborted is expected when the connection is disposed (and aborted)
                                        // while we're receiving a request header or sending a response.
                                        // TruncatedData is expected when reading a request header. It can also be
                                        // thrown when reading a response payload tied to an incoming IceRPC payload.
                                    }
                                    catch (InvalidDataException)
                                    {
                                        // This occurs when we can't decode the request header.
                                    }
                                    catch (OperationCanceledException exception) when (
                                        exception.CancellationToken == cancellationToken)
                                    {
                                        // This occurs during shutdown.
                                    }
                                    catch (Exception exception)
                                    {
                                        // not expected
                                        _faultedTaskAction(exception);
                                    }
                                    finally
                                    {
                                        lock (_mutex)
                                        {
                                            if (--_dispatchCount == 0 && _acceptRequestsTask!.IsCompleted)
                                            {
                                                _ = _dispatchesCompleted.TrySetResult();
                                            }
                                        }
                                    }
                                },
                                CancellationToken.None);
                        }
                    }
                    catch (OperationCanceledException exception) when (
                        exception.CancellationToken == _acceptStreamCts.Token)
                    {
                        // Expected if the connection is being shutdown.
                    }
                    catch (IceRpcException exception)
                    {
                        await DisposeTransportAsync("The connection was lost.", exception).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException exception)
                    {
                        await DisposeTransportAsync("The connection was disposed.", exception).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        await DisposeTransportAsync("The connection failed due to an unhandled exception.", exception)
                            .ConfigureAwait(false);
                        Debug.Fail($"The accept stream task completed due to an unhandled exception: {exception}");
                        throw;
                    }
                },
                CancellationToken.None);

            EnableIdleCheck();
            return transportConnectionInformation;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            RefuseNewInvocations("The connection was disposed.");
            _disposeTask ??= PerformDisposeAsync();
        }
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            // Make sure we execute the code below without holding the mutex lock.
            await Task.Yield();

            // We don't lock _mutex since once _disposeTask is not null, _connectTask etc are immutable.

            if (_connectTask is null)
            {
                _ = _closedTcs.TrySetResult(null); // disposing non-connected connection
            }
            else
            {
                try
                {
                    _ = await _connectTask.ConfigureAwait(false);
                }
                catch
                {
                    // ignore any ConnectAsync exception
                }

                if (_isShutdown)
                {
                    // ShutdownAsync will complete Closed and we wait for Closed completion.

                    _dispatchesAndInvocationsCts.Cancel(); // speed up shutdown
                    _ = await Closed.ConfigureAwait(false);
                }
                else
                {
                    // Abortive disposal.

                    _ = _closedTcs.TrySetResult(
                        new IceRpcException(IceRpcError.ConnectionAborted, "The connection was disposed."));
                }
            }

            await DisposeTransportAsync().ConfigureAwait(false);

            _acceptStreamCts.Cancel();
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

            lock (_mutex)
            {
                if (_streamCount == 0)
                {
                    _streamsClosed.TrySetResult();
                }
                if (_dispatchCount == 0)
                {
                    _dispatchesCompleted.TrySetResult();
                }
            }

            // Next, wait for dispatches to complete. We're not waiting for network activity on the streams to complete
            // (with _streamsClosed.Task). It should be complete since we've disposed the underlying transport
            // connection.
            await _dispatchesCompleted.Task.ConfigureAwait(false);

            // It's safe to complete the output since write operations have been completed by the connection
            // disposal.
            _controlStream?.Output.Complete();

            // It's safe to complete the input since read operations have been completed by the connection disposal.
            _remoteControlStream?.Input.Complete();

            _dispatchesAndInvocationsCts.Dispose();
            _acceptStreamCts.Dispose();
            _dispatchSemaphore?.Dispose();
            await _idleTimeoutTimer.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Protocol != ServerAddress.Protocol)
        {
            throw new InvalidOperationException(
                $"Cannot send {request.Protocol} request on {ServerAddress.Protocol} connection.");
        }

        lock (_mutex)
        {
            if (_disposeTask is not null)
            {
                throw new ObjectDisposedException($"{typeof(IceRpcProtocolConnection)}");
            }
            if (_refuseInvocations)
            {
                throw new IceRpcException(IceRpcError.OperationRefused, _operationRefusedMessage);
            }
            if (_connectTask is null)
            {
                throw new InvalidOperationException("Cannot invoke on a connection before connecting it.");
            }
            if (!_isServer && !_connectTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "Cannot invoke on a client connection that is not fully established.");
            }
            if (request.ServiceAddress.Fragment.Length > 0)
            {
                throw new NotSupportedException("The icerpc protocol does not support fragments.");
            }
            // It's possible but rare to invoke on a server connection that is still connecting.
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            var invocationCts = CancellationTokenSource.CreateLinkedTokenSource(
                _dispatchesAndInvocationsCts.Token);
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
                    stream = await _transportConnection.CreateStreamAsync(
                        bidirectional: !request.IsOneway,
                        invocationCancellationToken).ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    lock (_mutex)
                    {
                        Debug.Assert(_refuseInvocations);
                        throw new IceRpcException(IceRpcError.OperationRefused, _operationRefusedMessage);
                    }
                }

                streamInput = stream.IsBidirectional ? stream.Input : null;

                lock (_mutex)
                {
                    if (_refuseInvocations)
                    {
                        throw new IceRpcException(IceRpcError.OperationRefused, _operationRefusedMessage);
                    }

                    if (++_streamCount == 1)
                    {
                        DisableIdleCheck();
                    }

                    // Keep track of the invocation cancellation token source for the shutdown logic.
                    _pendingInvocations.Add(stream, invocationCts);
                    invocationCts = null; // invocationCts is disposed by UnregisterOnInputAndOutputCompletedAsync

                    _ = UnregisterOnInputAndOutputCompletedAsync(stream);
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

                if (_dispatchesAndInvocationsCts.IsCancellationRequested)
                {
                    // Abortive-shutdown canceled the request.
                    throw new IceRpcException(IceRpcError.OperationAborted);
                }
                else
                {
                    // TODO: Add IceRpcError.OperationCanceledByShutdown and throw
                    // IceRpcException(IceRpcError.OperationCanceledByShutdown) instead?

                    // Shutdown canceled the request because the peer didn't dispatch it.
                    lock (_mutex)
                    {
                        Debug.Assert(_refuseInvocations);
                        throw new IceRpcException(IceRpcError.OperationRefused, _operationRefusedMessage);
                    }
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
                throw new ObjectDisposedException($"{typeof(ProtocolConnection)}");
            }
            if (_isShutdown)
            {
                throw new InvalidOperationException("Cannot call shutdown more than once.");
            }
            if (_connectTask is null)
            {
                throw new InvalidOperationException("Cannot shut down a protocol connection before connecting it.");
            }

            _isShutdown = true;
            RefuseNewInvocations("The connection was shut down.");

            if (_closedTcs.Task.IsCompletedSuccessfully && _closedTcs.Task.Result is Exception abortException)
            {
                throw new IceRpcException(
                    IceRpcError.OperationRefused,
                    _connectTask.IsFaulted ? "The connection establishment failed." : "The connection was aborted.",
                    abortException);
            }
        }

        return PerformShutdownAsync();

        async Task PerformShutdownAsync()
        {
            try
            {
                // Wait for connect to complete first.
                if (_connectTask is not null)
                {
                    try
                    {
                        _ = await _connectTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception) when (
                        exception.CancellationToken != cancellationToken)
                    {
                        // ConnectAsync was canceled.
                        throw new IceRpcException(
                            IceRpcError.OperationAborted,
                            "The shutdown was aborted because the connection establishment was canceled.");
                    }
                }

                Debug.Assert(_acceptRequestsTask is not null);
                _acceptStreamCts.Cancel();
                await _acceptRequestsTask.ConfigureAwait(false);

                lock (_mutex)
                {
                    if (_streamCount == 0)
                    {
                        _streamsClosed.TrySetResult();
                    }
                    if (_dispatchCount == 0)
                    {
                        _dispatchesCompleted.TrySetResult();
                    }
                }

                // Once _acceptRequestsTask completes, _lastRemoteBidirectionalStreamId and
                // _lastRemoteUnidirectionalStreamId are immutable.

                // When this peer is the server endpoint, the first accepted bidirectional stream ID is 0. When this
                // peer is the client endpoint, the first accepted bidirectional stream ID is 1.
                IceRpcGoAway goAwayFrame = new(
                     _lastRemoteBidirectionalStreamId is ulong value ? value + 4 : (_isServer ? 0ul : 1ul),
                     (_lastRemoteUnidirectionalStreamId ?? _remoteControlStream!.Id) + 4);

                try
                {
                    await SendControlFrameAsync(
                        IceRpcControlFrameType.GoAway,
                        goAwayFrame.Encode,
                        cancellationToken).ConfigureAwait(false);
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
                    IceRpcGoAway peerGoAwayFrame = await _readGoAwayTask!.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);

                    // Abort streams for outgoing requests that were not dispatched by the peer. The invocations will
                    // throw IceRpcException(OperationRefused) which can be retried. Since _isShutdown is true,
                    // _pendingInvocations is immutable at this point.
                    foreach ((IMultiplexedStream stream, CancellationTokenSource cts) in _pendingInvocations)
                    {
                        if (!stream.IsStarted ||
                            stream.Id >= (stream.IsBidirectional ?
                                peerGoAwayFrame.BidirectionalStreamId : peerGoAwayFrame.UnidirectionalStreamId))
                        {
                            try
                            {
                                cts.Cancel();
                            }
                            catch (ObjectDisposedException)
                            {
                                // Expected if already disposed.
                            }
                        }
                    }

                    // Wait for network activity on streams (other than control streams) to cease.
                    await _streamsClosed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                    ReadResult readResult = await _remoteControlStream!.Input.ReadAsync(cancellationToken)
                        .ConfigureAwait(false);

                    Debug.Assert(!readResult.IsCanceled);

                    if (!readResult.IsCompleted || !readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException(
                            "Received bytes on the control stream after receiving the GoAway frame.");
                    }
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.ConnectionClosedByPeer)
                {
                    // Expected if the peer closed the connection first.
                }

                // We can now safely close the connection.
                try
                {
                    await _transportConnection.CloseAsync(
                        MultiplexedConnectionCloseError.NoError,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
                {
                    // Expected if the peer closed the connection first and the accept request loop closed the transport
                    // connection.
                }
                catch (ObjectDisposedException)
                {
                    // Expected if the peer closed the connection first and the accept request loop closed the transport
                    // connection.
                }

                // We wait for the completion of the dispatches that we created.
                await _dispatchesCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                _closedTcs.SetResult(null);
            }
            catch (OperationCanceledException)
            {
                _ = _closedTcs.TrySetResult(
                    new IceRpcException(IceRpcError.OperationAborted, "The shutdown was canceled."));
                throw;
            }
            catch (IceRpcException exception)
            {
                _ = _closedTcs.TrySetResult(exception);
                throw;
            }
            catch (Exception exception)
            {
                var newException = new IceRpcException(IceRpcError.IceRpcError, exception);
                _ = _closedTcs.TrySetResult(newException);
                throw newException;
            }
        }
    }

    internal IceRpcProtocolConnection(
        IMultiplexedConnection transportConnection,
        TransportConnectionInformation? transportConnectionInformation,
        ConnectionOptions options)
    {
        _isServer = transportConnectionInformation is not null;
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

        _idleTimeout = options.IdleTimeout;
        _idleTimeoutTimer = new Timer(_ =>
        {
            bool requestShutdown = false;

            lock (_mutex)
            {
                if (_dispatchCount == 0 && _streamCount == 0)
                {
                    requestShutdown = true;
                    RefuseNewInvocations(
                        $"The connection was shut down because it was idle for over {_idleTimeout.TotalSeconds} s.");
                    DisableIdleCheck();
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

    private void CheckPeerHeaderSize(int headerSize)
    {
        if (headerSize > _peerMaxHeaderSize)
        {
            throw new IceRpcException(
                IceRpcError.LimitExceeded,
                $"The header size ({headerSize}) for an icerpc request or response is greater than the peer's max header size ({_peerMaxHeaderSize}).");
        }
    }

    private void DisableIdleCheck() => _idleTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private async Task DispatchRequestAsync(IMultiplexedStream stream, CancellationToken cancellationToken)
    {
        PipeReader? fieldsPipeReader = null;
        PipeReader? streamInput = stream.Input;
        PipeWriter? streamOutput = stream.IsBidirectional ? stream.Output : null;
        IncomingRequest? request = null;
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

            await PerformDispatchRequestAsync(request).ConfigureAwait(false);
        }
        catch
        {
            // We always need to complete streamOutput when an exception is thrown. For example, we received an invalid
            // request header that we could not decode.
            streamOutput?.CompleteOutput(success: false);
            streamInput?.Complete();
            throw;
        }
        finally
        {
            fieldsPipeReader?.Complete();
            request?.Dispose();
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

    /// <summary>Marks the protocol connection as closed, disposes the transport connection and cancels pending
    /// dispatches and invocations.</summary>
    private async Task DisposeTransportAsync(string? message = null, Exception? exception = null)
    {
        // The connection can already be refusing invocations if being shutdown or disposed. In this case the connection
        // shutdown or disposal is responsible for completing _closedTcs.
        lock (_mutex)
        {
            if (!_refuseInvocations)
            {
                RefuseNewInvocations(message);
                var rpcException = exception as IceRpcException;
                if (exception is not null && rpcException is null)
                {
                    rpcException = new IceRpcException(IceRpcError.IceRpcError, exception);
                }
                _closedTcs.TrySetResult(rpcException);
            }
        }

        // Dispose the transport connection. This will trigger the failure of tasks waiting on transport operations.
        await _transportConnection.DisposeAsync().ConfigureAwait(false);

        // Cancel dispatches and invocations, there's no point in letting them continue once the connection is closed.
        _dispatchesAndInvocationsCts.Cancel();
    }

    private void EnableIdleCheck() => _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);

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

    private void RefuseNewInvocations(string? message)
    {
        lock (_mutex)
        {
            _refuseInvocations = true;
            _operationRefusedMessage ??= message;
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
                        exception.IceRpcError is IceRpcError.ConnectionAborted or
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
                CancellationToken.None);
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

    private async Task UnregisterOnInputAndOutputCompletedAsync(IMultiplexedStream stream)
    {
        // Wait for the stream's reading and writing side to be closed to unregister the stream from the connection.
        await Task.WhenAll(stream.ReadsClosed, stream.WritesClosed).ConfigureAwait(false);

        lock (_mutex)
        {
            if (!stream.IsRemote && _disposeTask is null && !_isShutdown)
            {
                if (_pendingInvocations.Remove(stream, out CancellationTokenSource? cts))
                {
                    cts.Dispose();
                }
                else
                {
                    Debug.Assert(false);
                }
            }

            if (--_streamCount == 0)
            {
                if (_acceptRequestsTask!.IsCompleted)
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
}
