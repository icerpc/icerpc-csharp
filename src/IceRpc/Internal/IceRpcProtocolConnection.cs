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
    public ServerAddress ServerAddress => _transportConnection.ServerAddress;

    private const int MaxGoAwayFrameBodySize = 16;
    private const int MaxSettingsFrameBodySize = 1024;

    private bool IsServer => _transportConnectionInformation is not null;

    private Task? _acceptRequestsTask;

    private Task? _connectTask;
    private IConnectionContext? _connectionContext; // non-null once the connection is established
    private IMultiplexedStream? _controlStream;
    private int _dispatchCount;
    private readonly IDispatcher? _dispatcher;
    private readonly TaskCompletionSource _dispatchesCompleted = new();

    private readonly SemaphoreSlim? _dispatchSemaphore;

    // This cancellation token source is canceled when the connection is disposed.
    private readonly CancellationTokenSource _disposedCts = new();

    private Task? _disposeTask;

    // The number of bytes we need to encode a size up to _maxRemoteHeaderSize. It's 2 for DefaultMaxHeaderSize.
    private int _headerSizeLength = 2;

    private readonly TimeSpan _inactivityTimeout;
    private readonly Timer _inactivityTimeoutTimer;

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
    private Task? _readGoAwayTask;

    // A connection refuses invocations when it's disposed, shut down, shutting down or merely "shutdown requested".
    private bool _refuseInvocations;

    private IMultiplexedStream? _remoteControlStream;

    private readonly CancellationTokenSource _shutdownCts;

    // The thread that completes this TCS can run the continuations, and as a result its result must be set without
    // holding a lock on _mutex.
    private readonly TaskCompletionSource _shutdownRequestedTcs = new();

    private Task? _shutdownTask;

    private int _streamCount;
    private readonly TaskCompletionSource _streamsClosed = new();

    private readonly ITaskExceptionObserver? _taskExceptionObserver;

    private readonly IMultiplexedConnection _transportConnection;

    // Only set for server connections.
    private readonly TransportConnectionInformation? _transportConnectionInformation;

    public Task<(TransportConnectionInformation ConnectionInformation, Task ShutdownRequested)> ConnectAsync(
        CancellationToken cancellationToken)
    {
        Task<(TransportConnectionInformation ConnectionInformation, Task ShutdownRequested)> result;

        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

            if (_connectTask is not null)
            {
                throw new InvalidOperationException("Cannot call connect more than once.");
            }

            result = PerformConnectAsync();
            _connectTask = result;
        }
        return result;

        async Task<(TransportConnectionInformation ConnectionInformation, Task ShutdownRequested)> PerformConnectAsync()
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
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Debug.Assert(_disposedCts.Token.IsCancellationRequested);
                throw new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The connection establishment was aborted because the connection was disposed.");
            }
            catch (InvalidDataException exception)
            {
                throw new IceRpcException(
                    IceRpcError.IceRpcError,
                    "The connection establishment was aborted by an icerpc protocol error.",
                    exception);
            }
            catch (AuthenticationException)
            {
                throw;
            }
            catch (IceRpcException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.Fail($"ConnectAsync failed with an unexpected exception: {exception}");
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

                // Read the go away frame from the control stream.
                _readGoAwayTask = ReadGoAwayAsync(_disposedCts.Token);

                // Start a task that accepts requests (the "accept requests loop")
                _acceptRequestsTask = AcceptRequestsAsync(_shutdownCts.Token);
            }

            // The _acceptRequestsTask waits for this PerformConnectAsync completion before reading anything. As soon as
            // it receives a request, it will cancel this inactivity check.
            ScheduleInactivityCheck();

            return (transportConnectionInformation, _shutdownRequestedTcs.Task);
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

            // We don't lock _mutex since once _disposeTask is not null, _connectTask etc are immutable.

            if (_connectTask is not null)
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
            _shutdownCts.Dispose();

            await _inactivityTimeoutTimer.DisposeAsync().ConfigureAwait(false);
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
        CancellationToken shutdownCancellationToken;
        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

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
            shutdownCancellationToken = _shutdownCts.Token;
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            var invocationCts = CancellationTokenSource.CreateLinkedTokenSource(disposedCancellationToken);
            try
            {
                IncomingResponse response = await PerformInvokeAsyncCore(invocationCts).ConfigureAwait(false);
                // PerformInvokeAsyncCore will dispose the invocationCts when the invocation is unregister
                // see UnregisterInvocationAsync.
                invocationCts = null;
                return response;
            }
            finally
            {
                invocationCts?.Dispose();
            }
        }

        async Task<IncomingResponse> PerformInvokeAsyncCore(CancellationTokenSource invocationCts)
        {
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
                    // We want to cancel CreateStreamAsync as soon as the connection is being shutdown instead of
                    // waiting for its disposal.
                    using CancellationTokenRegistration _ = shutdownCancellationToken.UnsafeRegister(
                        cts => ((CancellationTokenSource)cts!).Cancel(),
                        invocationCts);
                    stream = await _transportConnection.CreateStreamAsync(
                        bidirectional: !request.IsOneway,
                        invocationCancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Connection was shutdown or disposed and we did not read the payload at all.
                    throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage);
                }
                catch (IceRpcException exception)
                {
                    RefuseNewInvocations("The connection was lost.");
                    throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage, exception);
                }
                catch (Exception exception)
                {
                    Debug.Fail($"CreateStreamAsync failed with an unexpected exception: {exception}");
                    RefuseNewInvocations("The connection was lost.");
                    throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage, exception);
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
                        CancelInactivityCheck();
                    }

                    // Keep track of the invocation cancellation token source for the shutdown logic.
                    _pendingInvocations.Add(stream, invocationCts);
                    _ = UnregisterStreamOnReadsAndWritesClosedAsync(stream);
                }
            }
            catch
            {
                stream?.Output.CompleteOutput(success: false);
                streamInput?.Complete();
                throw;
            }

            PipeWriter payloadWriter;
            try
            {
                EncodeHeader(stream.Output);
                payloadWriter = request.GetPayloadWriter(stream.Output);
            }
            catch
            {
                stream.Output.CompleteOutput(success: false);
                throw;
            }

            try
            {
                bool hasContinuation = request.PayloadContinuation is not null;
                Task writesClosed = stream.WritesClosed;
                FlushResult flushResult;

                try
                {
                    flushResult = await payloadWriter.CopyFromAsync(
                        request.Payload,
                        writesClosed,
                        endStream: !hasContinuation,
                        invocationCancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    payloadWriter.CompleteOutput(success: false);
                    request.PayloadContinuation?.Complete();
                    throw;
                }
                finally
                {
                    request.Payload.Complete();
                }

                if (flushResult.IsCompleted || flushResult.IsCanceled || !hasContinuation)
                {
                    // The remote reader doesn't want more data, or the copying was canceled, or there is no
                    // continuation: we're done.
                    payloadWriter.CompleteOutput(!flushResult.IsCanceled);
                    request.PayloadContinuation?.Complete();
                }
                else
                {
                    // Send the continuation in a background task after "detaching" this continuation.
                    PipeReader payloadContinuation = request.PayloadContinuation!;
                    request.PayloadContinuation = null;

                    _ = Task.Run(
                        async () =>
                        {
                            var flushResult = new FlushResult(isCanceled: true, isCompleted: false);
                            try
                            {
                                // The cancellation of the token given to InvokeAsync cancels invocationCts only until
                                // InvokeAsync completes (see tokenRegistration); after that, the cancellation of this
                                // token has no effect on invocationCts, so it doesn't cancel the copying of
                                // payloadContinuation.
                                flushResult = await payloadWriter.CopyFromAsync(
                                    payloadContinuation,
                                    writesClosed,
                                    endStream: true,
                                    invocationCancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception exception) when (_taskExceptionObserver is not null)
                            {
                                _taskExceptionObserver.RequestPayloadContinuationFailed(
                                    request,
                                    _connectionContext!.TransportConnectionInformation,
                                    exception);
                            }
                            catch (OperationCanceledException exception) when (
                                exception.CancellationToken == invocationCancellationToken)
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
                                // TruncatedData is expected when the payloadContinuation comes from an incoming
                                // IceRPC payload and the peer's Output is completed with an exception.
                            }
                            catch (Exception exception)
                            {
                                // This exception is unexpected when running the IceRPC test suite. A test that expects
                                // such an exception must install a task exception observer.
                                Debug.Fail($"Failed to send payload continuation of request {request}: {exception}");

                                // If Debug is not enabled and there is no task exception observer, we rethrow to
                                // generate an Unobserved Task Exception.
                                throw;
                            }
                            finally
                            {
                                payloadWriter.CompleteOutput(!flushResult.IsCanceled);
                                payloadContinuation.Complete();
                            }
                        },
                        CancellationToken.None); // must run no matter what to complete the payload continuation
                }

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
                // UnregisterInvocationAsync will dispose the invocationCts
                _ = UnregisterInvocationAsync(stream);
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

            async Task UnregisterInvocationAsync(IMultiplexedStream stream)
            {
                // Wait for the stream's reading and writing side to be closed to dispose the invocation cts.
                await Task.WhenAll(stream.ReadsClosed, stream.WritesClosed).ConfigureAwait(false);
                lock (_mutex)
                {
                    if (!_refuseInvocations)
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
                }
            }
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

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
        return _shutdownTask;

        async Task PerformShutdownAsync()
        {
            await Task.Yield(); // exit mutex lock

            _shutdownCts.Cancel();

            try
            {
                Debug.Assert(_acceptRequestsTask is not null);
                Debug.Assert(_controlStream is not null);
                Debug.Assert(_readGoAwayTask is not null);
                Debug.Assert(_remoteControlStream is not null);

                await _acceptRequestsTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposedCts.Token);

                // Once shutdownTask is not null, _lastRemoteBidirectionalStreamId and _lastRemoteUnidirectionalStreamId
                // are immutable.

                // When this peer is the server endpoint, the first accepted bidirectional stream ID is 0. When this
                // peer is the client endpoint, the first accepted bidirectional stream ID is 1.
                IceRpcGoAway goAwayFrame = new(
                     _lastRemoteBidirectionalStreamId is ulong value ? value + 4 : (IsServer ? 0ul : 1ul),
                     (_lastRemoteUnidirectionalStreamId ?? _remoteControlStream.Id) + 4);

                try
                {
                    _ = await SendControlFrameAsync(
                        IceRpcControlFrameType.GoAway,
                        goAwayFrame.Encode,
                        cts.Token).ConfigureAwait(false);

                    // Wait for the peer to send back a GoAway frame. The task should already be completed if the
                    // shutdown was initiated by the peer.
                    await _readGoAwayTask.WaitAsync(cts.Token).ConfigureAwait(false);

                    // Wait for network activity on streams (other than control streams) to cease.
                    await _streamsClosed.Task.WaitAsync(cts.Token).ConfigureAwait(false);

                    // Close the control stream to notify the peer that on our side, all the streams completed and that
                    // it can close the transport connection whenever it likes.
                    _controlStream.Output.CompleteOutput(success: true);
                }
                catch
                {
                    // If we fail to send the GoAway frame or some other failure occur (such as
                    // OperationCanceledException) we are in an abortive closure and we close Output to allow
                    // the peer to continue if it's waiting for us.
                    _controlStream.Output.CompleteOutput(success: false);
                    throw;
                }

                // Wait for the peer notification that on its side all the streams are completed. It's important to wait
                // for this notification before closing the connection. In particular with Quic where closing the
                // connection before all the streams are processed could lead to a stream failure.
                try
                {
                    // Wait for the _remoteControlStream Input completion.
                    ReadResult readResult = await _remoteControlStream.Input.ReadAsync(cts.Token).ConfigureAwait(false);

                    Debug.Assert(!readResult.IsCanceled);

                    if (!readResult.IsCompleted || !readResult.Buffer.IsEmpty)
                    {
                        throw new InvalidDataException(
                            "Received bytes on the control stream after receiving the GoAway frame.");
                    }

                    // We can now safely close the connection.
                    await _transportConnection.CloseAsync(MultiplexedConnectionCloseError.NoError, cts.Token)
                        .ConfigureAwait(false);
                }
                catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.ConnectionClosedByPeer)
                {
                    // Expected if the peer closed the connection first.
                }

                // We wait for the completion of the dispatches that we created.
                await _dispatchesCompleted.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Debug.Assert(_disposedCts.Token.IsCancellationRequested);
                throw new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The connection shutdown was aborted because the connection was disposed.");
            }
            catch (InvalidDataException exception)
            {
                throw new IceRpcException(
                    IceRpcError.IceRpcError,
                    "The connection shutdown was aborted by an icerpc protocol error.",
                    exception);
            }
            catch (IceRpcException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.Fail($"ShutdownAsync failed with an unexpected exception: {exception}");
                throw;
            }
        }
    }

    internal IceRpcProtocolConnection(
        IMultiplexedConnection transportConnection,
        TransportConnectionInformation? transportConnectionInformation,
        ConnectionOptions options,
        ITaskExceptionObserver? taskExceptionObserver)
    {
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);

        _taskExceptionObserver = taskExceptionObserver;

        _transportConnection = transportConnection;
        _dispatcher = options.Dispatcher;
        _maxLocalHeaderSize = options.MaxIceRpcHeaderSize;
        _transportConnectionInformation = transportConnectionInformation;

        if (options.MaxDispatches > 0)
        {
            _dispatchSemaphore = new SemaphoreSlim(
                initialCount: options.MaxDispatches,
                maxCount: options.MaxDispatches);
        }

        _inactivityTimeout = options.InactivityTimeout;
        _inactivityTimeoutTimer = new Timer(_ =>
        {
            bool requestShutdown = false;

            lock (_mutex)
            {
                if (_dispatchCount == 0 && _streamCount == 0 && _shutdownTask is null)
                {
                    requestShutdown = true;
                    RefuseNewInvocations(
                        $"The connection was shut down because it was inactive for over {_inactivityTimeout.TotalSeconds} s.");
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
        await _connectTask!.ConfigureAwait(false);

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
                        // We were inactive, we no longer are.
                        CancelInactivityCheck();
                    }
                }

                _ = UnregisterStreamOnReadsAndWritesClosedAsync(stream);

                // Start a task to read the stream and dispatch the request. We pass CancellationToken.None
                // to Task.Run because DispatchRequestAsync must clean-up the stream.
                _ = Task.Run(() => DispatchRequestAsync(stream), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected, the associated cancellation token source was canceled.
        }
        catch (IceRpcException)
        {
            RefuseNewInvocations("The connection was lost");
            _ = _shutdownRequestedTcs.TrySetResult();
            throw;
        }
        catch (Exception exception)
        {
            Debug.Fail($"The accept stream task failed with an unexpected exception: {exception}");
            RefuseNewInvocations("The connection was lost");
            _ = _shutdownRequestedTcs.TrySetResult();
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

    private void CancelInactivityCheck() =>
        _inactivityTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private async Task DispatchRequestAsync(IMultiplexedStream stream)
    {
        // _disposedCts is not disposed since we own a dispatch count.
        using var dispatchCts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);
        Task? cancelDispatchOnWritesClosedTask = null;

        if (stream.IsBidirectional)
        {
            // If the peer is no longer interested in the response of the dispatch, we cancel the dispatch.
            cancelDispatchOnWritesClosedTask = CancelDispatchOnWritesClosedAsync();

            async Task CancelDispatchOnWritesClosedAsync()
            {
                try
                {
                    await stream.WritesClosed.WaitAsync(dispatchCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // ignored.
                }
                dispatchCts.Cancel();
            }
        }

        PipeReader? fieldsPipeReader = null;
        IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields;
        IceRpcRequestHeader header;

        PipeReader? streamInput = stream.Input;
        PipeWriter? streamOutput = stream.IsBidirectional ? stream.Output : null;
        bool success = false;

        try
        {
            try
            {
                ReadResult readResult = await streamInput.ReadSegmentAsync(
                    SliceEncoding.Slice2,
                    _maxLocalHeaderSize,
                    dispatchCts.Token).ConfigureAwait(false);

                if (readResult.Buffer.IsEmpty)
                {
                    throw new InvalidDataException("Received icerpc request with empty header.");
                }

                (header, fields, fieldsPipeReader) = DecodeHeader(readResult.Buffer);
                streamInput.AdvanceTo(readResult.Buffer.End);
            }
            catch (Exception exception) when (_taskExceptionObserver is not null)
            {
                _taskExceptionObserver.DispatchRefused(_connectionContext!.TransportConnectionInformation, exception);
                return; // success remains false
            }

            using var request = new IncomingRequest(_connectionContext!)
            {
                Fields = fields,
                IsOneway = !stream.IsBidirectional,
                Operation = header.Operation,
                Path = header.Path,
                Payload = streamInput
            };

            streamInput = null; // the request now owns streamInput

            try
            {
                await PerformDispatchRequestAsync(request, dispatchCts.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (_taskExceptionObserver is not null)
            {
                _taskExceptionObserver.DispatchFailed(
                    request,
                    _connectionContext!.TransportConnectionInformation,
                    exception);
                return; // success remains false
            }
            success = true;
        }
        catch (IceRpcException exception) when (exception.IceRpcError is IceRpcError.ConnectionAborted)
        {
            // ConnectionAborted is expected when the peer aborts the connection.
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == dispatchCts.Token)
        {
            // expected, dispatch canceled by peer or dispose
        }
        catch (Exception exception)
        {
            // This exception is unexpected when running the IceRPC test suite. A test that expects this exception must
            // install a task exception observer.
            Debug.Fail($"Dispatch failed with an unexpected exception: {exception}");

            // Generate unobserved task exception (UTE). If this exception is expected (e.g. an expected payload read
            // exception) and the application wants to avoid this UTE, it must configure a non-null logger to install
            // a task exception observer.
            throw;
        }
        finally
        {
            if (cancelDispatchOnWritesClosedTask is not null)
            {
                dispatchCts.Cancel();
                await cancelDispatchOnWritesClosedTask.ConfigureAwait(false);
            }

            if (!success)
            {
                // We always need to complete streamOutput when an exception is thrown. For example, we received an
                // invalid request header that we could not decode.
                streamOutput?.CompleteOutput(success: false);
                streamInput?.Complete();
            }
            fieldsPipeReader?.Complete();

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

            PipeWriter payloadWriter = response.GetPayloadWriter(streamOutput);

            // We give flushResult an initial "failed" value, in case the first CopyFromAsync throws.
            var flushResult = new FlushResult(isCanceled: true, isCompleted: false);

            try
            {
                bool hasContinuation = response.PayloadContinuation is not null;

                flushResult = await payloadWriter.CopyFromAsync(
                    response.Payload,
                    stream.WritesClosed,
                    endStream: !hasContinuation,
                    cancellationToken).ConfigureAwait(false);

                if (!flushResult.IsCompleted && !flushResult.IsCanceled && hasContinuation)
                {
                    flushResult = await payloadWriter.CopyFromAsync(
                        response.PayloadContinuation!,
                        stream.WritesClosed,
                        endStream: true,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                payloadWriter.CompleteOutput(success: !flushResult.IsCanceled);
                response.Payload.Complete();
                response.PayloadContinuation?.Complete();
            }

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

    // The inactivity check executes once in _inactivityTimeout. By then either:
    // - the connection is no longer inactive (and CancelInactivityCheck was called or is being called)
    // - the connection is still inactive and we request shutdown
    private void ScheduleInactivityCheck() =>
        _inactivityTimeoutTimer.Change(_inactivityTimeout, Timeout.InfiniteTimeSpan);

    private async Task ReadGoAwayAsync(CancellationToken cancellationToken)
    {
        await Task.Yield(); // exit mutex lock

        // Wait for _connectTask (which spawned the task running this method) to complete. This await can't fail.
        // This guarantees this method won't request a shutdown until after _connectTask completed successfully.
        await _connectTask!.ConfigureAwait(false);

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

            RefuseNewInvocations("The connection was shut down because it received a GoAway frame from the peer.");

            // Abort streams for outgoing requests that were not dispatched by the peer. The invocations will
            // throw IceRpcException(InvocationCanceled) which can be retried. Since _refuseInvocations is true,
            // _pendingInvocations is immutable at this point.
            foreach ((IMultiplexedStream stream, CancellationTokenSource invocationCts) in _pendingInvocations)
            {
                if (!stream.IsStarted ||
                    stream.Id >= (stream.IsBidirectional ?
                        goAwayFrame.BidirectionalStreamId : goAwayFrame.UnidirectionalStreamId))
                {
                    invocationCts.Cancel();
                }
            }

            _ = _shutdownRequestedTcs.TrySetResult();
        }
        catch (OperationCanceledException)
        {
            // The connection is disposed and we let this exception cancel the task.
            throw;
        }
        catch (IceRpcException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "The ReadGoAway task was aborted by an icerpc protocol error.",
                exception);
        }
        catch (Exception exception)
        {
            Debug.Fail($"The read go away task failed with an unexpected exception: {exception}");
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

    private async Task UnregisterStreamOnReadsAndWritesClosedAsync(IMultiplexedStream stream)
    {
        // Wait for the stream's reading and writing side to be closed to unregister the stream from the connection.
        await Task.WhenAll(stream.ReadsClosed, stream.WritesClosed).ConfigureAwait(false);

        lock (_mutex)
        {
            if (--_streamCount == 0)
            {
                if (_shutdownTask is not null)
                {
                    _streamsClosed.TrySetResult();
                }
                else if (!_refuseInvocations)
                {
                    // We enable the inactivity check in order to complete ShutdownRequested when inactive for too long.
                    // _refuseInvocations is true when the connection is either about to be "shutdown requested", or
                    // shut down / disposed, or aborted (with Closed completed). We don't need to complete
                    // ShutdownRequested in any of these situations.
                    ScheduleInactivityCheck();
                }
            }
        }
    }
}
