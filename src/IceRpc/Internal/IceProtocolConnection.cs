// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Security.Authentication;
using ZeroC.Slice;

namespace IceRpc.Internal;

/// <summary>Implements <see cref="IProtocolConnection" /> for the ice protocol.</summary>
internal sealed class IceProtocolConnection : IProtocolConnection
{
    private static readonly IDictionary<RequestFieldKey, ReadOnlySequence<byte>> _idempotentFields =
        new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
        {
            [RequestFieldKey.Idempotent] = default
        }.ToImmutableDictionary();

    private bool IsServer => _transportConnectionInformation is not null;

    private IConnectionContext? _connectionContext; // non-null once the connection is established
    private Task? _connectTask;
    private readonly IDispatcher _dispatcher;

    // The number of outstanding dispatches and invocations.
    private int _dispatchInvocationCount;

    // We don't want the continuation to run from the dispatch or invocation thread.
    private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly SemaphoreSlim? _dispatchSemaphore;

    // This cancellation token source is canceled when the connection is disposed.
    private readonly CancellationTokenSource _disposedCts = new();

    private Task? _disposeTask;
    private readonly IDuplexConnection _duplexConnection;
    private readonly DuplexConnectionReader _duplexConnectionReader;
    private readonly IceDuplexConnectionWriter _duplexConnectionWriter;
    private bool _heartbeatEnabled = true;
    private Task _heartbeatTask = Task.CompletedTask;
    private readonly TimeSpan _inactivityTimeout;
    private readonly Timer _inactivityTimeoutTimer;
    private string? _invocationRefusedMessage;
    private int _lastRequestId;
    private readonly int _maxFrameSize;
    private readonly object _mutex = new();
    private readonly PipeOptions _pipeOptions;
    private Task? _readFramesTask;

    // A connection refuses invocations when it's disposed, shut down, shutting down or merely "shutdown requested".
    private bool _refuseInvocations;

    // Does ShutdownAsync send a close connection frame?
    private bool _sendCloseConnectionFrame = true;

    private Task? _shutdownTask;

    // The thread that completes this TCS can run the continuations, and as a result its result must be set without
    // holding a lock on _mutex.
    private readonly TaskCompletionSource _shutdownRequestedTcs = new();

    // Only set for server connections.
    private readonly TransportConnectionInformation? _transportConnectionInformation;

    private readonly CancellationTokenSource _twowayDispatchesCts;
    private readonly Dictionary<int, TaskCompletionSource<PipeReader>> _twowayInvocations = new();

    private Exception? _writeException; // protected by _writeSemaphore
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

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
                    await _duplexConnection.ConnectAsync(connectCts.Token).ConfigureAwait(false);

                if (IsServer)
                {
                    // Send ValidateConnection frame.
                    await SendControlFrameAsync(EncodeValidateConnectionFrame, connectCts.Token).ConfigureAwait(false);

                    // The SendControlFrameAsync is a "write" that schedules a keep-alive when the idle timeout is not
                    // infinite.
                }
                else
                {
                    ReadOnlySequence<byte> buffer = await _duplexConnectionReader.ReadAtLeastAsync(
                        IceDefinitions.PrologueSize,
                        connectCts.Token).ConfigureAwait(false);

                    (IcePrologue validateConnectionFrame, long consumed) = DecodeValidateConnectionFrame(buffer);
                    _duplexConnectionReader.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

                    IceDefinitions.CheckPrologue(validateConnectionFrame);
                    if (validateConnectionFrame.FrameSize != IceDefinitions.PrologueSize)
                    {
                        throw new InvalidDataException(
                            $"Received ice frame with only '{validateConnectionFrame.FrameSize}' bytes.");
                    }
                    if (validateConnectionFrame.FrameType != IceFrameType.ValidateConnection)
                    {
                        throw new InvalidDataException(
                            $"Expected '{nameof(IceFrameType.ValidateConnection)}' frame but received frame type '{validateConnectionFrame.FrameType}'.");
                    }
                }
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
                    IceRpcError.ConnectionAborted,
                    "The connection was aborted by an ice protocol error.",
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

            // We assign _readFramesTask with _mutex locked to make sure this assignment occurs before the start of
            // DisposeAsync. Once _disposeTask is not null, _readFramesTask is immutable.
            lock (_mutex)
            {
                if (_disposeTask is not null)
                {
                    throw new IceRpcException(
                        IceRpcError.OperationAborted,
                        "The connection establishment was aborted because the connection was disposed.");
                }

                // This needs to be set before starting the read frames task below.
                _connectionContext = new ConnectionContext(this, transportConnectionInformation);

                _readFramesTask = ReadFramesAsync(_disposedCts.Token);
            }

            // The _readFramesTask waits for this PerformConnectAsync completion before reading anything. As soon as
            // it receives a request, it will cancel this inactivity check.
            ScheduleInactivityCheck();

            return (transportConnectionInformation, _shutdownRequestedTcs.Task);

            static void EncodeValidateConnectionFrame(IBufferWriter<byte> writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
            }

            static (IcePrologue, long) DecodeValidateConnectionFrame(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, SliceEncoding.Slice1);
                return (new IcePrologue(ref decoder), decoder.Consumed);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            if (_disposeTask is null)
            {
                RefuseNewInvocations("The connection was disposed.");

                _shutdownTask ??= Task.CompletedTask;
                if (_dispatchInvocationCount == 0)
                {
                    _dispatchesAndInvocationsCompleted.TrySetResult();
                }

                _heartbeatEnabled = false; // makes _heartbeatTask immutable

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
                // Wait for all writes to complete. This can't take forever since all writes are canceled by
                // _disposedCts.Token.
                await _writeSemaphore.WaitAsync().ConfigureAwait(false);

                try
                {
                    await Task.WhenAll(
                        _connectTask,
                        _readFramesTask ?? Task.CompletedTask,
                        _heartbeatTask,
                        _dispatchesAndInvocationsCompleted.Task,
                        _shutdownTask).ConfigureAwait(false);
                }
                catch
                {
                    // Expected if any of these tasks failed or was canceled. Each task takes care of handling
                    // unexpected exceptions so there's no need to handle them here.
                }
            }

            _duplexConnection.Dispose();

            // It's safe to dispose the reader/writer since no more threads are sending/receiving data.
            _duplexConnectionReader.Dispose();
            _duplexConnectionWriter.Dispose();

            _disposedCts.Dispose();
            _twowayDispatchesCts.Dispose();

            _dispatchSemaphore?.Dispose();
            _writeSemaphore.Dispose();
            await _inactivityTimeoutTimer.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Protocol != Protocol.Ice)
        {
            throw new InvalidOperationException(
                $"Cannot send {request.Protocol} request on {Protocol.Ice} connection.");
        }

        lock (_mutex)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

            if (_refuseInvocations)
            {
                throw new IceRpcException(IceRpcError.InvocationRefused, _invocationRefusedMessage);
            }
            if (_connectTask is null || !_connectTask.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException("Cannot invoke on a connection that is not fully established.");
            }

            IncrementDispatchInvocationCount();
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            // Since _dispatchInvocationCount > 0, _disposedCts is not disposed.
            using var invocationCts =
                CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token, cancellationToken);

            PipeReader? frameReader = null;
            TaskCompletionSource<PipeReader>? responseCompletionSource = null;
            int requestId = 0;

            try
            {
                // Read the full payload. This can take some time so this needs to be done before acquiring the write
                // semaphore.
                ReadOnlySequence<byte> payloadBuffer = await ReadFullPayloadAsync(request.Payload, invocationCts.Token)
                    .ConfigureAwait(false);

                try
                {
                    // Wait for the writing of other frames to complete.
                    using SemaphoreLock _ = await AcquireWriteLockAsync(invocationCts.Token).ConfigureAwait(false);

                    // Assign the request ID for two-way invocations and keep track of the invocation for receiving the
                    // response. The request ID is only assigned once the write semaphore is acquired. We don't want a
                    // canceled request to allocate a request ID that won't be used.
                    lock (_mutex)
                    {
                        if (_refuseInvocations)
                        {
                            // It's InvocationCanceled and not InvocationRefused because we've read the payload.
                            throw new IceRpcException(IceRpcError.InvocationCanceled, _invocationRefusedMessage);
                        }

                        if (!request.IsOneway)
                        {
                            // wrap around back to 1 if we reach int.MaxValue. 0 means one-way.
                            _lastRequestId = _lastRequestId == int.MaxValue ? 1 : _lastRequestId + 1;
                            requestId = _lastRequestId;

                            // RunContinuationsAsynchronously because we don't want the "read frames loop" to run the
                            // continuation.
                            responseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                            _twowayInvocations[requestId] = responseCompletionSource;
                        }
                    }

                    int payloadSize = checked((int)payloadBuffer.Length);

                    try
                    {
                        EncodeRequestHeader(_duplexConnectionWriter, request, requestId, payloadSize);

                        // We write to the duplex connection with _disposedCts.Token instead of invocationCts.Token.
                        // Canceling this write operation is fatal to the connection.
                        await _duplexConnectionWriter.WriteAsync(payloadBuffer, _disposedCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        WriteFailed(exception);
                        throw;
                    }
                }
                catch (IceRpcException exception) when (exception.IceRpcError != IceRpcError.InvocationCanceled)
                {
                    // Since we could not send the request, the server cannot dispatch it and it's safe to retry.
                    // This includes the situation where await AcquireWriteLockAsync throws because a previous write
                    // failed.
                    throw new IceRpcException(
                        IceRpcError.InvocationCanceled,
                        "Failed to send ice request.",
                        exception);
                }
                finally
                {
                    // We've read the payload (see ReadFullPayloadAsync) and we are now done with it.
                    request.Payload.Complete();
                }

                if (request.IsOneway)
                {
                    // We're done, there's no response for one-way requests.
                    return new IncomingResponse(request, _connectionContext!);
                }

                // Wait to receive the response.
                Debug.Assert(responseCompletionSource is not null);
                frameReader = await responseCompletionSource.Task.WaitAsync(invocationCts.Token).ConfigureAwait(false);

                if (!frameReader.TryRead(out ReadResult readResult))
                {
                    throw new InvalidDataException($"Received empty response frame for request with id '{requestId}'.");
                }

                Debug.Assert(readResult.IsCompleted);

                (StatusCode statusCode, string? errorMessage, SequencePosition consumed) =
                    DecodeResponseHeader(readResult.Buffer, requestId);

                frameReader.AdvanceTo(consumed);

                var response = new IncomingResponse(
                    request,
                    _connectionContext!,
                    statusCode,
                    errorMessage)
                {
                    Payload = frameReader
                };

                frameReader = null; // response now owns frameReader
                return response;
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Debug.Assert(_disposedCts.Token.IsCancellationRequested);
                throw new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The invocation was aborted because the connection was disposed.");
            }
            finally
            {
                // If responseCompletionSource is not completed, we want to complete it to prevent another method from
                // setting an unobservable exception in it. And if it's already completed with an exception, we observe
                // this exception.
                if (responseCompletionSource is not null &&
                    !responseCompletionSource.TrySetResult(InvalidPipeReader.Instance))
                {
                    try
                    {
                        _ = await responseCompletionSource.Task.ConfigureAwait(false);
                    }
                    catch
                    {
                        // observe exception, if any
                    }
                }

                lock (_mutex)
                {
                    // Unregister the two-way invocation if registered.
                    if (requestId > 0 && !_refuseInvocations)
                    {
                        _twowayInvocations.Remove(requestId);
                    }

                    DecrementDispatchInvocationCount();
                }

                frameReader?.Complete();
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

            if (_dispatchInvocationCount == 0)
            {
                _dispatchesAndInvocationsCompleted.TrySetResult();
            }
            _shutdownTask = PerformShutdownAsync(_sendCloseConnectionFrame);
        }

        return _shutdownTask;

        async Task PerformShutdownAsync(bool sendCloseConnectionFrame)
        {
            await Task.Yield(); // exit mutex lock

            try
            {
                Debug.Assert(_readFramesTask is not null);

                // Since DisposeAsync waits for the _shutdownTask completion, _disposedCts is not disposed at this
                // point.
                using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _disposedCts.Token);

                // Wait for dispatches and invocations to complete.
                await _dispatchesAndInvocationsCompleted.Task.WaitAsync(shutdownCts.Token).ConfigureAwait(false);

                // Stops sending heartbeats. We can't do earlier: while we're waiting for dispatches and invocations to
                // complete, we need to keep sending heartbeats otherwise the peer could see the connection as idle and
                // abort it.
                lock (_mutex)
                {
                    _heartbeatEnabled = false; // makes _heartbeatTask immutable
                }

                // Wait for the last send heartbeat to complete before sending the CloseConnection frame or disposing
                // the duplex connection. _heartbeatTask is immutable once _shutdownTask set. _heartbeatTask can be
                // canceled by DisposeAsync.
                await _heartbeatTask.WaitAsync(shutdownCts.Token).ConfigureAwait(false);

                if (sendCloseConnectionFrame)
                {
                    // Send CloseConnection frame.
                    await SendControlFrameAsync(EncodeCloseConnectionFrame, shutdownCts.Token).ConfigureAwait(false);

                    // Wait for the peer to abort the connection as an acknowledgment for this CloseConnection frame.
                    // The peer can also send us a CloseConnection frame if it started shutting down at the same time.
                    // We can't just return and dispose the duplex connection since the peer can still be reading frames
                    // (including the CloseConnection frame) and we don't want to abort this reading.
                    await _readFramesTask.WaitAsync(shutdownCts.Token).ConfigureAwait(false);
                }
                else
                {
                    // _readFramesTask should be already completed or nearly completed.
                    await _readFramesTask.WaitAsync(shutdownCts.Token).ConfigureAwait(false);

                    // _readFramesTask succeeded means the peer is waiting for us to abort the duplex connection;
                    // we oblige.
                    _duplexConnection.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Debug.Assert(_disposedCts.Token.IsCancellationRequested);
                throw new IceRpcException(
                    IceRpcError.OperationAborted,
                    "The connection shutdown was aborted because the connection was disposed.");
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

            static void EncodeCloseConnectionFrame(IBufferWriter<byte> writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.CloseConnectionFrame.Encode(ref encoder);
            }
        }
    }

    internal IceProtocolConnection(
        IDuplexConnection duplexConnection,
        TransportConnectionInformation? transportConnectionInformation,
        ConnectionOptions options)
    {
        _twowayDispatchesCts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);

        // With ice, we always listen for incoming frames (responses) so we need a dispatcher for incoming requests even
        // if we don't expect any. This dispatcher throws an ice ObjectNotExistException back to the client, which makes
        // more sense than throwing an UnknownException.
        _dispatcher = options.Dispatcher ?? NotFoundDispatcher.Instance;

        _maxFrameSize = options.MaxIceFrameSize;
        _transportConnectionInformation = transportConnectionInformation;

        if (options.MaxDispatches > 0)
        {
            _dispatchSemaphore = new SemaphoreSlim(
                initialCount: options.MaxDispatches,
                maxCount: options.MaxDispatches);
        }

        _inactivityTimeout = options.InactivityTimeout;

        // The readerScheduler doesn't matter (we don't call pipe.Reader.ReadAsync on the resulting pipe), and the
        // writerScheduler doesn't matter (pipe.Writer.FlushAsync never blocks).
        _pipeOptions = new PipeOptions(
            pool: options.Pool,
            minimumSegmentSize: options.MinSegmentSize,
            pauseWriterThreshold: 0,
            useSynchronizationContext: false);

        if (options.IceIdleTimeout != Timeout.InfiniteTimeSpan)
        {
            duplexConnection = new IceDuplexConnectionDecorator(
                duplexConnection,
                readIdleTimeout: options.EnableIceIdleCheck ? options.IceIdleTimeout : Timeout.InfiniteTimeSpan,
                writeIdleTimeout: options.IceIdleTimeout,
                SendHeartbeat);
        }

        _duplexConnection = duplexConnection;
        _duplexConnectionReader = new DuplexConnectionReader(_duplexConnection, options.Pool, options.MinSegmentSize);
        _duplexConnectionWriter =
            new IceDuplexConnectionWriter(_duplexConnection, options.Pool, options.MinSegmentSize);

        _inactivityTimeoutTimer = new Timer(_ =>
        {
            bool requestShutdown = false;

            lock (_mutex)
            {
                if (_dispatchInvocationCount == 0 && _shutdownTask is null)
                {
                    requestShutdown = true;
                    RefuseNewInvocations(
                        $"The connection was shut down because it was inactive for over {_inactivityTimeout.TotalSeconds} s.");
                }
            }

            if (requestShutdown)
            {
                // TrySetResult must be called outside the mutex lock.
                _shutdownRequestedTcs.TrySetResult();
            }
        });

        void SendHeartbeat()
        {
            lock (_mutex)
            {
                if (_heartbeatTask.IsCompletedSuccessfully && _heartbeatEnabled)
                {
                    _heartbeatTask = SendValidateConnectionFrameAsync(_disposedCts.Token);
                }
            }

            async Task SendValidateConnectionFrameAsync(CancellationToken cancellationToken)
            {
                // Make sure we execute the function without holding the connection mutex lock.
                await Task.Yield();

                try
                {
                    await SendControlFrameAsync(EncodeValidateConnectionFrame, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Canceled by DisposeAsync
                    throw;
                }
                catch (IceRpcException)
                {
                    // Expected, typically the peer aborted the connection.
                    throw;
                }
                catch (Exception exception)
                {
                    Debug.Fail($"The heartbeat task completed due to an unhandled exception: {exception}");
                    throw;
                }

                static void EncodeValidateConnectionFrame(IBufferWriter<byte> writer)
                {
                    var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                    IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
                }
            }
        }
    }

    private static (int RequestId, IceRequestHeader Header, PipeReader? ContextReader, int Consumed) DecodeRequestIdAndHeader(
        ReadOnlySequence<byte> buffer)
    {
        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice1);

        int requestId = decoder.DecodeInt32();

        var requestHeader = new IceRequestHeader(ref decoder);

        Pipe? contextPipe = null;
        long pos = decoder.Consumed;
        int count = decoder.DecodeSize();
        if (count > 0)
        {
            for (int i = 0; i < count; ++i)
            {
                decoder.Skip(decoder.DecodeSize()); // Skip the key
                decoder.Skip(decoder.DecodeSize()); // Skip the value
            }
            contextPipe = new Pipe();
            contextPipe.Writer.Write(buffer.Slice(pos, decoder.Consumed - pos));
            contextPipe.Writer.Complete();
        }

        var encapsulationHeader = new EncapsulationHeader(ref decoder);

        if (encapsulationHeader.PayloadEncodingMajor != 1 ||
            encapsulationHeader.PayloadEncodingMinor != 1)
        {
            throw new InvalidDataException(
                $"Unsupported payload encoding '{encapsulationHeader.PayloadEncodingMajor}.{encapsulationHeader.PayloadEncodingMinor}'.");
        }

        int payloadSize = encapsulationHeader.EncapsulationSize - 6;
        if (payloadSize != (buffer.Length - decoder.Consumed))
        {
            throw new InvalidDataException(
                $"Request payload size mismatch: expected {payloadSize} bytes, read {buffer.Length - decoder.Consumed} bytes.");
        }

        return (requestId, requestHeader, contextPipe?.Reader, (int)decoder.Consumed);
    }

    private static (StatusCode StatusCode, string? ErrorMessage, SequencePosition Consumed) DecodeResponseHeader(
        ReadOnlySequence<byte> buffer,
        int requestId)
    {
        ReplyStatus replyStatus = ((int)buffer.FirstSpan[0]).AsReplyStatus();

        if (replyStatus <= ReplyStatus.UserException)
        {
            const int headerSize = 7; // reply status byte + encapsulation header

            // read and check encapsulation header (6 bytes long)

            if (buffer.Length < headerSize)
            {
                throw new InvalidDataException($"Received invalid frame header for request with id '{requestId}'.");
            }

            EncapsulationHeader encapsulationHeader = SliceEncoding.Slice1.DecodeBuffer(
                buffer.Slice(1, 6),
                (ref SliceDecoder decoder) => new EncapsulationHeader(ref decoder));

            // Sanity check
            int payloadSize = encapsulationHeader.EncapsulationSize - 6;
            if (payloadSize != buffer.Length - headerSize)
            {
                throw new InvalidDataException(
                    $"Response payload size/frame size mismatch: payload size is {payloadSize} bytes but frame has {buffer.Length - headerSize} bytes left.");
            }

            SequencePosition consumed = buffer.GetPosition(headerSize);

            return replyStatus == ReplyStatus.Ok ? (StatusCode.Ok, null, consumed) :
                // Set the error message to the empty string. We will convert this empty string to null when we
                // decode the exception.
                (StatusCode.ApplicationError, "", consumed);
        }
        else
        {
            // An ice system exception.

            StatusCode statusCode = replyStatus switch
            {
                ReplyStatus.ObjectNotExistException => StatusCode.NotFound,
                ReplyStatus.FacetNotExistException => StatusCode.NotFound,
                ReplyStatus.OperationNotExistException => StatusCode.NotImplemented,
                _ => StatusCode.InternalError
            };

            var decoder = new SliceDecoder(buffer.Slice(1), SliceEncoding.Slice1);

            string message;
            switch (replyStatus)
            {
                case ReplyStatus.FacetNotExistException:
                case ReplyStatus.ObjectNotExistException:
                case ReplyStatus.OperationNotExistException:

                    var requestFailed = new RequestFailedExceptionData(ref decoder);

                    string target = requestFailed.Fragment.Length > 0 ?
                        $"{requestFailed.Identity.ToPath()}#{requestFailed.Fragment}" : requestFailed.Identity.ToPath();

                    message =
                        $"The dispatch failed with status code {statusCode} while dispatching '{requestFailed.Operation}' on '{target}'.";
                    break;
                default:
                    message = decoder.DecodeString();
                    break;
            }
            decoder.CheckEndOfBuffer();
            return (statusCode, message, buffer.End);
        }
    }

    private static void EncodeRequestHeader(
        IceDuplexConnectionWriter output,
        OutgoingRequest request,
        int requestId,
        int payloadSize)
    {
        var encoder = new SliceEncoder(output, SliceEncoding.Slice1);

        // Write the request header.
        encoder.WriteByteSpan(IceDefinitions.FramePrologue);
        encoder.EncodeIceFrameType(IceFrameType.Request);
        encoder.EncodeUInt8(0); // compression status

        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);

        encoder.EncodeInt32(requestId);

        byte encodingMajor = 1;
        byte encodingMinor = 1;

        // Request header.
        var requestHeader = new IceRequestHeader(
            Identity.Parse(request.ServiceAddress.Path),
            request.ServiceAddress.Fragment,
            request.Operation,
            request.Fields.ContainsKey(RequestFieldKey.Idempotent) ? OperationMode.Idempotent : OperationMode.Normal);
        requestHeader.Encode(ref encoder);
        int directWriteSize = 0;
        if (request.Fields.TryGetValue(RequestFieldKey.Context, out OutgoingFieldValue requestField))
        {
            if (requestField.WriteAction is Action<IBufferWriter<byte>> writeAction)
            {
                // This writes directly to the underlying output; we measure how many bytes are written.
                long start = output.UnflushedBytes;
                writeAction(output);
                directWriteSize = (int)(output.UnflushedBytes - start);
            }
            else
            {
                encoder.WriteByteSequence(requestField.ByteSequence);
            }
        }
        else
        {
            encoder.EncodeSize(0);
        }

        // We ignore all other fields. They can't be sent over ice.

        new EncapsulationHeader(
            encapsulationSize: payloadSize + 6,
            encodingMajor,
            encodingMinor).Encode(ref encoder);

        int frameSize = checked(encoder.EncodedByteCount + directWriteSize + payloadSize);
        SliceEncoder.EncodeInt32(frameSize, sizePlaceholder);
    }

    private static void EncodeResponseHeader(
        IBufferWriter<byte> writer,
        OutgoingResponse response,
        IncomingRequest request,
        int requestId,
        int payloadSize)
    {
        var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);

        // Write the response header.

        encoder.WriteByteSpan(IceDefinitions.FramePrologue);
        encoder.EncodeIceFrameType(IceFrameType.Reply);
        encoder.EncodeUInt8(0); // compression status
        Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);

        encoder.EncodeInt32(requestId);

        if (response.StatusCode > StatusCode.ApplicationError ||
            (response.StatusCode == StatusCode.ApplicationError && payloadSize == 0))
        {
            // system exception
            switch (response.StatusCode)
            {
                case StatusCode.NotFound:
                case StatusCode.NotImplemented:
                    encoder.EncodeReplyStatus(response.StatusCode == StatusCode.NotFound ?
                        ReplyStatus.ObjectNotExistException : ReplyStatus.OperationNotExistException);

                    new RequestFailedExceptionData(Identity.Parse(request.Path), request.Fragment, request.Operation)
                        .Encode(ref encoder);
                    break;
                case StatusCode.InternalError:
                    encoder.EncodeReplyStatus(ReplyStatus.UnknownException);
                    encoder.EncodeString(response.ErrorMessage!);
                    break;
                default:
                    encoder.EncodeReplyStatus(ReplyStatus.UnknownException);
                    encoder.EncodeString(
                        $"{response.ErrorMessage} {{ Original StatusCode = {response.StatusCode} }}");
                    break;
            }
        }
        else
        {
            encoder.EncodeReplyStatus((ReplyStatus)response.StatusCode);

            // When IceRPC receives a response, it ignores the response encoding. So this "1.1" is only relevant to
            // a ZeroC Ice client that decodes the response. The only Slice encoding such a client can possibly use
            // to decode the response payload is 1.1 or 1.0, and we don't care about interop with 1.0.
            var encapsulationHeader = new EncapsulationHeader(
                encapsulationSize: payloadSize + 6,
                payloadEncodingMajor: 1,
                payloadEncodingMinor: 1);
            encapsulationHeader.Encode(ref encoder);
        }

        int frameSize = encoder.EncodedByteCount + payloadSize;
        SliceEncoder.EncodeInt32(frameSize, sizePlaceholder);
    }

    /// <summary>Reads the full Ice payload from the given pipe reader.</summary>
    private static async ValueTask<ReadOnlySequence<byte>> ReadFullPayloadAsync(
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        // We use ReadAtLeastAsync instead of ReadAsync to bypass the PauseWriterThreshold when the payload is
        // backed by a Pipe.
        ReadResult readResult = await payload.ReadAtLeastAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);

        if (readResult.IsCanceled)
        {
            throw new InvalidOperationException("Unexpected call to CancelPendingRead on ice payload.");
        }

        return readResult.IsCompleted ? readResult.Buffer :
            throw new ArgumentException("The payload size is greater than int.MaxValue.", nameof(payload));
    }

    /// <summary>Acquires exclusive access to _duplexConnectionWriter.</summary>
    /// <returns>A <see cref="SemaphoreLock" /> that releases the acquired semaphore in its Dispose method.</returns>
    private async ValueTask<SemaphoreLock> AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        SemaphoreLock semaphoreLock = await _writeSemaphore.AcquireAsync(cancellationToken).ConfigureAwait(false);

        // _writeException is protected by _writeSemaphore
        if (_writeException is not null)
        {
            semaphoreLock.Dispose();

            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "The connection was aborted because a previous write operation failed.",
                _writeException);
        }

        return semaphoreLock;
    }

    /// <summary>Creates a pipe reader to simplify the reading of a request or response frame. The frame is read fully
    /// and buffered into an internal pipe.</summary>
    private async ValueTask<PipeReader> CreateFrameReaderAsync(int size, CancellationToken cancellationToken)
    {
        var pipe = new Pipe(_pipeOptions);

        try
        {
            await _duplexConnectionReader.FillBufferWriterAsync(pipe.Writer, size, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            pipe.Reader.Complete();
            throw;
        }
        finally
        {
            pipe.Writer.Complete();
        }

        return pipe.Reader;
    }

    private void DecrementDispatchInvocationCount()
    {
        lock (_mutex)
        {
            if (--_dispatchInvocationCount == 0)
            {
                if (_shutdownTask is not null)
                {
                    _dispatchesAndInvocationsCompleted.TrySetResult();
                }
                // We enable the inactivity check in order to complete ShutdownRequested when inactive for too long.
                // _refuseInvocations is true when the connection is either about to be "shutdown requested", or shut
                // down / disposed. We don't need to complete ShutdownRequested in any of these situations.
                else if (!_refuseInvocations)
                {
                    ScheduleInactivityCheck();
                }
            }
        }
    }

    /// <summary>Dispatches an incoming request. This method executes in a task spawn from the read frames loop.
    /// </summary>
    private async Task DispatchRequestAsync(IncomingRequest request, int requestId, PipeReader? contextReader)
    {
        CancellationToken cancellationToken = request.IsOneway ? _disposedCts.Token : _twowayDispatchesCts.Token;

        OutgoingResponse? response;
        try
        {
            // The dispatcher can complete the incoming request payload to release its memory as soon as possible.
            try
            {
                // _dispatcher.DispatchAsync may very well ignore the cancellation token and we don't want to keep
                // dispatching when the cancellation token is canceled.
                cancellationToken.ThrowIfCancellationRequested();

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
            // ignored since we're not returning anything
            response = null;
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
        {
            // expected when the connection is disposed or the request is canceled by the peer's shutdown
            response = null;
        }
        catch (Exception exception)
        {
            if (exception is not DispatchException dispatchException)
            {
                dispatchException = new DispatchException(StatusCode.InternalError, innerException: exception);
            }
            response = dispatchException.ToOutgoingResponse(request);
        }
        finally
        {
            request.Payload.Complete();
            contextReader?.Complete();

            // The field values are now invalid - they point to potentially recycled and reused memory. We
            // replace Fields by an empty dictionary to prevent accidental access to this reused memory.
            request.Fields = ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty;
        }

        try
        {
            if (response is not null)
            {
                // Read the full response payload. This can take some time so this needs to be done before acquiring
                // the write semaphore.
                ReadOnlySequence<byte> payload = ReadOnlySequence<byte>.Empty;

                if (response.StatusCode <= StatusCode.ApplicationError)
                {
                    try
                    {
                        payload = await ReadFullPayloadAsync(response.Payload, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // We "encode" the exception in the error message.

                        response = new OutgoingResponse(
                            request,
                            StatusCode.InternalError,
                            "The dispatch failed to read the response payload.",
                            exception);
                    }
                }
                // else payload remains empty because the payload of a dispatch exception (if any) cannot be sent
                // over ice.

                int payloadSize = checked((int)payload.Length);

                // Wait for writing of other frames to complete.
                using SemaphoreLock _ = await AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    EncodeResponseHeader(_duplexConnectionWriter, response, request, requestId, payloadSize);

                    // We write to the duplex connection with _disposedCts.Token instead of cancellationToken.
                    // Canceling this write operation is fatal to the connection.
                    await _duplexConnectionWriter.WriteAsync(payload, _disposedCts.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    WriteFailed(exception);
                    throw;
                }
            }
        }
        catch (OperationCanceledException exception) when (
            exception.CancellationToken == _disposedCts.Token ||
            exception.CancellationToken == cancellationToken)
        {
            // expected when the connection is disposed or the request is canceled by the peer's shutdown
        }
        finally
        {
            DecrementDispatchInvocationCount();
        }
    }

    /// <summary>Increments the dispatch-invocation count.</summary>
    /// <remarks>This method must be called with _mutex locked.</remarks>
    private void IncrementDispatchInvocationCount()
    {
        if (_dispatchInvocationCount++ == 0)
        {
            // Cancel inactivity check.
            _inactivityTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void ScheduleInactivityCheck() =>
        _inactivityTimeoutTimer.Change(_inactivityTimeout, Timeout.InfiniteTimeSpan);

    /// <summary>Reads incoming frames and returns successfully when a CloseConnection frame is received or when the
    /// connection is aborted during ShutdownAsync or canceled by DisposeAsync.</summary>
    private async Task ReadFramesAsync(CancellationToken cancellationToken)
    {
        await Task.Yield(); // exit mutex lock

        // Wait for _connectTask (which spawned the task running this method) to complete. This way, we won't dispatch
        // any request until _connectTask has completed successfully, and indirectly we won't make any invocation until
        // _connectTask has completed successfully. The creation of the _readFramesTask is the last action taken by
        // _connectTask and as a result this await can't fail.
        await _connectTask!.ConfigureAwait(false);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadOnlySequence<byte> buffer = await _duplexConnectionReader.ReadAtLeastAsync(
                    IceDefinitions.PrologueSize,
                    cancellationToken).ConfigureAwait(false);

                // First decode and check the prologue.

                ReadOnlySequence<byte> prologueBuffer = buffer.Slice(0, IceDefinitions.PrologueSize);

                IcePrologue prologue = SliceEncoding.Slice1.DecodeBuffer(
                    prologueBuffer,
                    (ref SliceDecoder decoder) => new IcePrologue(ref decoder));

                _duplexConnectionReader.AdvanceTo(prologueBuffer.End);

                IceDefinitions.CheckPrologue(prologue);
                if (prologue.FrameSize > _maxFrameSize)
                {
                    throw new InvalidDataException(
                        $"Received frame with size ({prologue.FrameSize}) greater than max frame size.");
                }

                if (prologue.CompressionStatus == 2)
                {
                    // The exception handler calls ReadFailed.
                    throw new IceRpcException(
                        IceRpcError.ConnectionAborted,
                        "The connection was aborted because it received a compressed ice frame, and IceRPC does not support ice compression.");
                }

                // Then process the frame based on its type.
                switch (prologue.FrameType)
                {
                    case IceFrameType.CloseConnection:
                    {
                        if (prologue.FrameSize != IceDefinitions.PrologueSize)
                        {
                            throw new InvalidDataException(
                                $"Received {nameof(IceFrameType.CloseConnection)} frame with unexpected data.");
                        }

                        lock (_mutex)
                        {
                            RefuseNewInvocations(
                                "The connection was shut down because it received a CloseConnection frame from the peer.");

                            // By exiting the "read frames loop" below, we are refusing new dispatches as well.

                            // Only one side sends the CloseConnection frame.
                            _sendCloseConnectionFrame = false;
                        }

                        // Even though we're in the "read frames loop", it's ok to cancel CTS and a "synchronous" TCS
                        // below. We won't be reading anything else so it's ok to run continuations synchronously.

                        // Abort two-way invocations that are waiting for a response (it will never come).
                        AbortTwowayInvocations(
                            IceRpcError.InvocationCanceled,
                            "The invocation was canceled by the shutdown of the peer.");

                        // Cancel two-way dispatches since the peer is not interested in the responses. This does not
                        // cancel ongoing writes to _duplexConnection: we don't send incomplete/invalid data.
                        _twowayDispatchesCts.Cancel();

                        // We keep sending heartbeats. If the shutdown request / shutdown is not fulfilled quickly, they
                        // tell the peer we're still alive and maybe stuck waiting for invocations and dispatches to
                        // complete.

                        // We request a shutdown that will dispose _duplexConnection once all invocations and dispatches
                        // have completed.
                        _shutdownRequestedTcs.TrySetResult();
                        return;
                    }

                    case IceFrameType.Request:
                        await ReadRequestAsync(prologue.FrameSize, cancellationToken).ConfigureAwait(false);
                        break;

                    case IceFrameType.RequestBatch:
                        // The exception handler calls ReadFailed.
                        throw new IceRpcException(
                            IceRpcError.ConnectionAborted,
                            "The connection was aborted because it received a batch request, and IceRPC does not support them.");

                    case IceFrameType.Reply:
                        await ReadReplyAsync(prologue.FrameSize, cancellationToken).ConfigureAwait(false);
                        break;

                    case IceFrameType.ValidateConnection:
                    {
                        if (prologue.FrameSize != IceDefinitions.PrologueSize)
                        {
                            throw new InvalidDataException(
                                $"Received {nameof(IceFrameType.ValidateConnection)} frame with unexpected data.");
                        }
                        break;
                    }

                    default:
                    {
                        throw new InvalidDataException(
                            $"Received Ice frame with unknown frame type '{prologue.FrameType}'.");
                    }
                }
            } // while
        }
        catch (OperationCanceledException)
        {
            // canceled by DisposeAsync, no need to throw anything
        }
        catch (IceRpcException exception) when (
            exception.IceRpcError == IceRpcError.ConnectionAborted &&
            _dispatchesAndInvocationsCompleted.Task.IsCompleted)
        {
            // The peer acknowledged receipt of the CloseConnection frame by aborting the duplex connection. Return.
            // See ShutdownAsync.
        }
        catch (IceRpcException exception)
        {
            ReadFailed(exception);
            throw;
        }
        catch (InvalidDataException exception)
        {
            ReadFailed(exception);
            throw new IceRpcException(
                IceRpcError.ConnectionAborted,
                "The connection was aborted by an ice protocol error.",
                exception);
        }
        catch (Exception exception)
        {
            Debug.Fail($"The read frames task completed due to an unhandled exception: {exception}");
            ReadFailed(exception);
            throw;
        }

        // Aborts all pending two-way invocations. Must be called outside the mutex lock after setting
        // _refuseInvocations to true.
        void AbortTwowayInvocations(IceRpcError error, string message, Exception? exception = null)
        {
            Debug.Assert(_refuseInvocations);

            // _twowayInvocations is immutable once _refuseInvocations is true.
            foreach (TaskCompletionSource<PipeReader> responseCompletionSource in _twowayInvocations.Values)
            {
                // _twowayInvocations can hold completed completion sources.
                _ = responseCompletionSource.TrySetException(new IceRpcException(error, message, exception));
            }
        }

        // Takes appropriate action after a read failure.
        void ReadFailed(Exception exception)
        {
            // We also prevent new one-way invocations even though they don't need to read the connection.
            RefuseNewInvocations("The connection was lost because a read operation failed.");

            // It's ok to cancel CTS and a "synchronous" TCS below. We won't be reading anything else so it's ok to run
            // continuations synchronously.

            AbortTwowayInvocations(
                IceRpcError.ConnectionAborted,
                "The invocation was aborted because the connection was lost.",
                exception);

            // ReadFailed is called when the connection is dead or the peer sent us a non-supported frame (e.g. a
            // batch request). We don't need to allow outstanding two-way dispatches to complete in these situations, so
            // we cancel them to speed-up the shutdown.
            _twowayDispatchesCts.Cancel();

            lock (_mutex)
            {
                // Don't send a close connection frame since we can't wait for the peer's acknowledgment.
                _sendCloseConnectionFrame = false;
            }

            _ = _shutdownRequestedTcs.TrySetResult();
        }
    }

    /// <summary>Reads a reply (incoming response) and completes the invocation response completion source with this
    /// response. This method executes "synchronously" in the read frames loop.</summary>
    private async Task ReadReplyAsync(int replyFrameSize, CancellationToken cancellationToken)
    {
        // Read the remainder of the frame immediately into frameReader.
        PipeReader replyFrameReader = await CreateFrameReaderAsync(
            replyFrameSize - IceDefinitions.PrologueSize,
            cancellationToken).ConfigureAwait(false);

        bool completeFrameReader = true;

        try
        {
            // Read and decode request ID
            if (!replyFrameReader.TryRead(out ReadResult readResult) || readResult.Buffer.Length < 4)
            {
                throw new InvalidDataException("Received a response with an invalid request ID.");
            }

            ReadOnlySequence<byte> requestIdBuffer = readResult.Buffer.Slice(0, 4);
            int requestId = SliceEncoding.Slice1.DecodeBuffer(
                requestIdBuffer,
                (ref SliceDecoder decoder) => decoder.DecodeInt32());
            replyFrameReader.AdvanceTo(requestIdBuffer.End);

            lock (_mutex)
            {
                if (_twowayInvocations.TryGetValue(
                    requestId,
                    out TaskCompletionSource<PipeReader>? responseCompletionSource))
                {
                    // continuation runs asynchronously
                    if (responseCompletionSource.TrySetResult(replyFrameReader))
                    {
                        completeFrameReader = false;
                    }
                    // else this invocation just completed and is about to remove itself from _twowayInvocations,
                    // or _twowayInvocations is immutable and contains entries for completed invocations.
                }
                // else the request ID carried by the response is bogus or corresponds to a request that was previously
                // discarded (for example, because its deadline expired).
            }
        }
        finally
        {
            if (completeFrameReader)
            {
                replyFrameReader.Complete();
            }
        }
    }

    /// <summary>Reads and then dispatches an incoming request in a separate dispatch task. This method executes
    /// "synchronously" in the read frames loop.</summary>
    private async Task ReadRequestAsync(int requestFrameSize, CancellationToken cancellationToken)
    {
        // Read the request frame.
        PipeReader requestFrameReader = await CreateFrameReaderAsync(
            requestFrameSize - IceDefinitions.PrologueSize,
            cancellationToken).ConfigureAwait(false);

        // Decode its header.
        int requestId;
        IceRequestHeader requestHeader;
        PipeReader? contextReader = null;
        IDictionary<RequestFieldKey, ReadOnlySequence<byte>>? fields;
        Task? dispatchTask = null;

        try
        {
            if (!requestFrameReader.TryRead(out ReadResult readResult))
            {
                throw new InvalidDataException("Received an invalid request frame.");
            }

            Debug.Assert(readResult.IsCompleted);

            (requestId, requestHeader, contextReader, int consumed) = DecodeRequestIdAndHeader(readResult.Buffer);
            requestFrameReader.AdvanceTo(readResult.Buffer.GetPosition(consumed));

            if (contextReader is null)
            {
                fields = requestHeader.OperationMode == OperationMode.Normal ?
                    ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty : _idempotentFields;
            }
            else
            {
                contextReader.TryRead(out ReadResult result);
                Debug.Assert(result.Buffer.Length > 0 && result.IsCompleted);
                fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>()
                {
                    [RequestFieldKey.Context] = result.Buffer
                };

                if (requestHeader.OperationMode != OperationMode.Normal)
                {
                    // OperationMode can be Idempotent or Nonmutating.
                    fields[RequestFieldKey.Idempotent] = default;
                }
            }

            bool releaseDispatchSemaphore = false;
            if (_dispatchSemaphore is SemaphoreSlim dispatchSemaphore)
            {
                // This prevents us from receiving any new frames if we're already dispatching the maximum number
                // of requests. We need to do this in the "accept from network loop" to apply back pressure to the
                // caller.
                try
                {
                    await dispatchSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    releaseDispatchSemaphore = true;
                }
                catch (OperationCanceledException)
                {
                    // and return below
                }
            }

            lock (_mutex)
            {
                if (_shutdownTask is not null)
                {
                    // The connection is (being) disposed or the connection is shutting down and received a request.
                    // We simply discard it. For a graceful shutdown, the two-way invocation in the peer will throw
                    // IceRpcException(InvocationCanceled). We also discard one-way requests: if we accepted them, they
                    // could delay our shutdown and make it time out.
                    if (releaseDispatchSemaphore)
                    {
                        _dispatchSemaphore!.Release();
                    }
                    return;
                }

                IncrementDispatchInvocationCount();
            }

            // The scheduling of the task can't be canceled since we want to make sure DispatchRequestAsync will
            // cleanup (decrement _dispatchCount etc.) if DisposeAsync is called. dispatchTask takes ownership of the
            // requestFrameReader and contextReader.
            dispatchTask = Task.Run(
                async () =>
                {
                    using var request = new IncomingRequest(Protocol.Ice, _connectionContext!)
                    {
                        Fields = fields,
                        Fragment = requestHeader.Fragment,
                        IsOneway = requestId == 0,
                        Operation = requestHeader.Operation,
                        Path = requestHeader.Identity.ToPath(),
                        Payload = requestFrameReader,
                    };

                    try
                    {
                        await DispatchRequestAsync(
                            request,
                            requestId,
                            contextReader).ConfigureAwait(false);
                    }
                    catch (IceRpcException)
                    {
                        // expected when the peer aborts the connection.
                    }
                    catch (Exception exception)
                    {
                        // With ice, a dispatch cannot throw an exception that comes from the application code:
                        // any exception thrown when reading the response payload is converted into a DispatchException
                        // response, and the response header has no fields to encode.
                        Debug.Fail($"ice dispatch {request} failed with an unexpected exception: {exception}");
                        throw;
                    }
                },
                CancellationToken.None);
        }
        finally
        {
            if (dispatchTask is null)
            {
                requestFrameReader.Complete();
                contextReader?.Complete();
            }
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

    /// <summary>Sends a control frame. It takes care of acquiring and releasing the write lock and calls
    /// <see cref="WriteFailed" /> if a failure occurs while writing to _duplexConnectionWriter.</summary>
    /// <param name="encode">Encodes the control frame.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>If the cancellation token is canceled while writing to the duplex connection, the connection is
    /// aborted.</remarks>
    private async ValueTask SendControlFrameAsync(
        Action<IBufferWriter<byte>> encode,
        CancellationToken cancellationToken)
    {
        using SemaphoreLock _ = await AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            encode(_duplexConnectionWriter);
            await _duplexConnectionWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WriteFailed(exception);
            throw;
        }
    }

    /// <summary>Takes appropriate action after a write failure.</summary>
    /// <remarks>Must be called outside the mutex lock but after acquiring _writeSemaphore.</remarks>
    private void WriteFailed(Exception exception)
    {
        Debug.Assert(_writeException is null);
        _writeException = exception; // protected by _writeSemaphore

        // We can't send new invocations without writing to the connection.
        RefuseNewInvocations("The connection was lost because a write operation failed.");

        // We can't send responses so these dispatches can be canceled.
        _twowayDispatchesCts.Cancel();

        // We don't change _sendClosedConnectionFrame. If the _readFrameTask is still running, we want ShutdownAsync
        // to send CloseConnection - and fail.

        _ = _shutdownRequestedTcs.TrySetResult();
    }
}
