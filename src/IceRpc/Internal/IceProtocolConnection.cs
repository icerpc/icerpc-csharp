// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal;

internal sealed class IceProtocolConnection : IProtocolConnection
{
    public Protocol Protocol => Protocol.Ice;

    private static readonly IDictionary<RequestFieldKey, ReadOnlySequence<byte>> _idempotentFields =
        new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
        {
            [RequestFieldKey.Idempotent] = default
        }.ToImmutableDictionary();

    private static readonly IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> _otherReplicaFields =
        new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
        {
            [ResponseFieldKey.RetryPolicy] = new ReadOnlySequence<byte>(new byte[]
            {
                (byte)Retryable.OtherReplica
            })
        }.ToImmutableDictionary();

    private readonly CancellationTokenSource _disposeCancelSource = new();
    private readonly IDispatcher _dispatcher;
    private readonly TaskCompletionSource _dispatchesAndInvocationsCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly HashSet<CancellationTokenSource> _dispatches = new();
    private readonly AsyncSemaphore? _dispatchSemaphore;
    private readonly TimeSpan _idleTimeout;
    private Timer? _idleTimeoutTimer;
    private readonly Dictionary<int, TaskCompletionSource<PipeReader>> _invocations = new();
    private readonly bool _isServer;
    private readonly int _maxFrameSize;
    private readonly MemoryPool<byte> _memoryPool;
    private readonly int _minimumSegmentSize;
    private readonly object _mutex = new();
    private readonly ISimpleNetworkConnection _networkConnection;
    private readonly SimpleNetworkConnectionReader _networkConnectionReader;
    private readonly SimpleNetworkConnectionWriter _networkConnectionWriter;
    private int _nextRequestId;
    private Action<Exception>? _onAbort;
    private Action<string>? _onShutdown;
    private readonly IcePayloadPipeWriter _payloadWriter;
    private readonly TaskCompletionSource _pendingClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _pingTask = Task.CompletedTask;
    private Task? _readFramesTask;
    private readonly CancellationTokenSource _shutdownCancelSource = new();
    private Task? _shutdownTask;
    private readonly AsyncSemaphore _writeSemaphore = new(1, 1);

    public async Task<NetworkConnectionInformation> ConnectAsync(IConnection connection, CancellationToken cancel)
    {
        NetworkConnectionInformation information = await _networkConnection.ConnectAsync(cancel).ConfigureAwait(false);

        // Wait for the network connection establishment to set the idle timeout. The network connection ConnectAsync
        // implementation would need otherwise to deal with thread safety if Dispose is called concurrently.
        _networkConnectionReader.SetIdleTimeout(_idleTimeout);

        if (_isServer)
        {
            EncodeValidateConnectionFrame(_networkConnectionWriter);
            await _networkConnectionWriter.FlushAsync(cancel).ConfigureAwait(false);
        }
        else
        {
            ReadOnlySequence<byte> buffer = await _networkConnectionReader.ReadAtLeastAsync(
                IceDefinitions.PrologueSize,
                cancel).ConfigureAwait(false);

            (IcePrologue validateConnectionFrame, long consumed) = DecodeValidateConnectionFrame(buffer);
            _networkConnectionReader.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

            IceDefinitions.CheckPrologue(validateConnectionFrame);
            if (validateConnectionFrame.FrameSize != IceDefinitions.PrologueSize)
            {
                throw new InvalidDataException(
                    $"received Ice frame with only '{validateConnectionFrame.FrameSize}' bytes");
            }
            if (validateConnectionFrame.FrameType != IceFrameType.ValidateConnection)
            {
                throw new InvalidDataException(
                    @$"expected '{nameof(IceFrameType.ValidateConnection)}' frame but received frame type '{
                        validateConnectionFrame.FrameType}'");
            }
        }

        if (_idleTimeout != Timeout.InfiniteTimeSpan)
        {
            _idleTimeoutTimer = new Timer(
                _ =>
                {
                    lock (_mutex)
                    {
                        if (_invocations.Count > 0 || _dispatches.Count > 0)
                        {
                            return; // The connection is closing or no longer idle, ignore.
                        }
                        _shutdownTask ??= ShutdownAsyncCore("idle connection", _shutdownCancelSource.Token);
                    }
                },
                null,
                _idleTimeout,
                Timeout.InfiniteTimeSpan);
        }

        _readFramesTask = Task.Run(
            async () =>
            {
                Exception? exception = null;
                try
                {
                    // Read frames until the CloseConnection frame is received.
                    await ReadFramesAsync(connection, _disposeCancelSource.Token).ConfigureAwait(false);

                    // Notify the OnShutdown callback.
                    InvokeOnShutdown("connection shutdown by peer");

                    exception = new ConnectionClosedException("connection shutdown by peer");
                }
                catch (ConnectionLostException) when (
                    _shutdownTask is not null &&
                    _dispatchesAndInvocationsCompleted.Task.IsCompleted)
                {
                    // Unblock ShutdownAsync which might be waiting for the connection to be disposed.
                    _pendingClose.TrySetResult();
                }
                catch (OperationCanceledException) when (_disposeCancelSource.IsCancellationRequested)
                {
                    // Expected if DisposeAsync has been called.
                }
                catch (Exception ex)
                {
                    // Unexpected exception, notify the OnAbort callback.
                    InvokeOnAbort(ex);
                    exception = ex;
                }
                finally
                {
                    // Cancel invocations and dispatches
                    if (exception != null)
                    {
                        IEnumerable<TaskCompletionSource<PipeReader>> invocations;
                        IEnumerable<CancellationTokenSource> dispatches;
                        lock (_mutex)
                        {
                            _shutdownTask = Task.CompletedTask; // Prevent new invocations from being accepted.
                            invocations = _invocations.Values.ToArray();
                            dispatches = _dispatches.ToArray();
                        }

                        foreach (TaskCompletionSource<PipeReader> responseCompletionSource in invocations)
                        {
                            responseCompletionSource.TrySetException(exception);
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
                    }
                }
            },
            CancellationToken.None);

        return information;

        static void EncodeValidateConnectionFrame(SimpleNetworkConnectionWriter writer)
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

    public async ValueTask DisposeAsync()
    {
        IEnumerable<CancellationTokenSource> dispatches;
        lock (_mutex)
        {
            _shutdownTask ??= Task.CompletedTask;
            if (_dispatches.Count == 0 && _invocations.Count == 0)
            {
                _dispatchesAndInvocationsCompleted.TrySetResult();
            }
            dispatches = _dispatches.ToArray();
        }

        // Cancel dispatches.
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

        // Cancel the ReadFrameAsync, ShutdownAsyncCore and PingAsync tasks.
        _disposeCancelSource.Cancel();
        _shutdownCancelSource.Cancel();

        // Wait indefinitely for dispatches and invocations to complete.
        await _dispatchesAndInvocationsCompleted.Task.ConfigureAwait(false);

        if (_readFramesTask is not null)
        {
            await _readFramesTask.ConfigureAwait(false);
        }

        try
        {
            await _shutdownTask.ConfigureAwait(false);
        }
        catch
        {
            // Expected if shutdown canceled or failed.
        }

        await _pingTask.ConfigureAwait(false);

        // No more pending tasks are running, we can safely release the resources now.

        // It's now safe to dispose of the reader/writer since no more threads are sending/receiving data.
        _networkConnectionReader.Dispose();
        _networkConnectionWriter.Dispose();
        _networkConnection.Dispose();

        _disposeCancelSource.Dispose();
        _shutdownCancelSource.Dispose();
        _idleTimeoutTimer?.Dispose();
    }

    public async Task<IncomingResponse> InvokeAsync(
        OutgoingRequest request,
        IConnection connection,
        CancellationToken cancel)
    {
        bool acquiredSemaphore = false;
        int requestId = 0;
        TaskCompletionSource<PipeReader>? responseCompletionSource = null;
        PipeWriter payloadWriter = _payloadWriter;

        using var linkedCancelSource = CancellationTokenSource.CreateLinkedTokenSource(
            _disposeCancelSource.Token,
            cancel);

        Exception? completeException = null;
        try
        {
            if (request.PayloadStream is not null)
            {
                throw new NotSupportedException("PayloadStream must be null with the ice protocol");
            }

            // Read the full payload. This can take some time so this needs to be done before acquiring the write
            // semaphore.
            ReadOnlySequence<byte> payload = await ReadFullPayloadAsync(
                request.Payload,
                linkedCancelSource.Token).ConfigureAwait(false);
            int payloadSize = checked((int)payload.Length);

            // Wait for writing of other frames to complete. The semaphore is used as an asynchronous queue to
            // serialize the writing of frames.
            await _writeSemaphore.EnterAsync(linkedCancelSource.Token).ConfigureAwait(false);
            acquiredSemaphore = true;

            // Assign the request ID for twoway invocations and keep track of the invocation for receiving the
            // response. The request ID is only assigned once the write semaphore is acquired. We don't want a
            // canceled request to allocate a request ID that won't be used.
            if (!request.IsOneway)
            {
                lock (_mutex)
                {
                    if (_shutdownTask is not null)
                    {
                        throw new ConnectionClosedException();
                    }
                    else
                    {
                        if (_invocations.Count == 0 && _dispatches.Count == 0)
                        {
                            // Disable idle check
                            _idleTimeoutTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                        }

                        requestId = ++_nextRequestId;
                        responseCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        _invocations[requestId] = responseCompletionSource;
                    }
                }
            }

            EncodeRequestHeader(_networkConnectionWriter, request, requestId, payloadSize);

            payloadWriter = request.GetPayloadWriter(payloadWriter);

            // The writing of the request can only be canceled if the connection is disposed.
            FlushResult flushResult = await payloadWriter.WriteAsync(
                payload,
                endStream: false,
                _disposeCancelSource.Token).ConfigureAwait(false);

            // If a payload writer decorator returns a canceled or completed flush result, we have to throw
            // NotSupportedException. We can't interrupt the writing of a payload since it would lead to a bogus
            // payload to be sent over the connection.
            if (flushResult.IsCanceled || flushResult.IsCompleted)
            {
                throw new NotSupportedException(
                    "payload writer cancellation or completion is not supported with the ice protocol");
            }

            await request.Payload.CompleteAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposeCancelSource.IsCancellationRequested)
        {
            completeException = new ConnectionAbortedException();
            throw completeException;
        }
        catch (Exception exception)
        {
            completeException = exception;
            throw;
        }
        finally
        {
            await payloadWriter.CompleteAsync(completeException).ConfigureAwait(false);

            if (acquiredSemaphore)
            {
                _writeSemaphore.Release();
            }
        }

        if (request.IsOneway)
        {
            // We're done, there's no response for oneway requests.
            return new IncomingResponse(request, connection);
        }

        Debug.Assert(responseCompletionSource is not null);

        // Wait to receive the response.
        try
        {
            PipeReader frameReader = await responseCompletionSource.Task.WaitAsync(
                linkedCancelSource.Token).ConfigureAwait(false);

            try
            {
                if (!frameReader.TryRead(out ReadResult readResult))
                {
                    throw new InvalidDataException($"received empty response frame for request #{requestId}");
                }

                Debug.Assert(readResult.IsCompleted);

                ReplyStatus replyStatus = ((int)readResult.Buffer.FirstSpan[0]).AsReplyStatus();

                if (replyStatus <= ReplyStatus.UserException)
                {
                    const int headerSize = 7; // reply status byte + encapsulation header

                    // read and check encapsulation header (6 bytes long)

                    if (readResult.Buffer.Length < headerSize)
                    {
                        throw new ConnectionLostException();
                    }

                    EncapsulationHeader encapsulationHeader = SliceEncoding.Slice1.DecodeBuffer(
                        readResult.Buffer.Slice(1, 6),
                        (ref SliceDecoder decoder) => new EncapsulationHeader(ref decoder));

                    // Sanity check
                    int payloadSize = encapsulationHeader.EncapsulationSize - 6;
                    if (payloadSize != readResult.Buffer.Length - headerSize)
                    {
                        throw new InvalidDataException(
                            @$"response payload size/frame size mismatch: payload size is {payloadSize
                            } bytes but frame has {readResult.Buffer.Length - headerSize} bytes left");
                    }

                    // TODO: check encoding is Slice1. See github proposal.

                    // Consume header.
                    frameReader.AdvanceTo(readResult.Buffer.GetPosition(headerSize));
                }
                else
                {
                    // An ice system exception. The reply status is part of the payload.

                    // Don't consume anything. The examined is irrelevant since readResult.IsCompleted is true.
                    frameReader.AdvanceTo(readResult.Buffer.Start);
                }

                // For compatibility with ZeroC Ice "indirect" proxies
                IDictionary<ResponseFieldKey, ReadOnlySequence<byte>> fields =
                    replyStatus == ReplyStatus.ObjectNotExistException && request.Proxy.Endpoint is null ?
                    _otherReplicaFields :
                    ImmutableDictionary<ResponseFieldKey, ReadOnlySequence<byte>>.Empty;

                return new IncomingResponse(request, connection, fields)
                {
                    Payload = frameReader,
                    ResultType = replyStatus switch
                    {
                        ReplyStatus.OK => ResultType.Success,
                        ReplyStatus.UserException => (ResultType)SliceResultType.ServiceFailure,
                        _ => ResultType.Failure
                    }
                };
            }
            catch (Exception exception)
            {
                await frameReader.CompleteAsync(exception).ConfigureAwait(false);
                throw;
            }
        }
        catch (OperationCanceledException) when (_disposeCancelSource.IsCancellationRequested)
        {
            throw new ConnectionAbortedException("connection disposed");
        }
        finally
        {
            lock (_mutex)
            {
                if (_invocations.Remove(requestId))
                {
                    if (_invocations.Count == 0 && _dispatches.Count == 0)
                    {
                        if (_shutdownTask is not null)
                        {
                            _dispatchesAndInvocationsCompleted.TrySetResult();
                        }
                        else if (!_disposeCancelSource.IsCancellationRequested)
                        {
                            _idleTimeoutTimer?.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                        }
                    }
                }
            }
        }

        static void EncodeRequestHeader(
            SimpleNetworkConnectionWriter output,
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
                request.Proxy.Path,
                request.Proxy.Fragment,
                request.Operation,
                request.Fields.ContainsKey(RequestFieldKey.Idempotent) ?
                    OperationMode.Idempotent : OperationMode.Normal);
            requestHeader.Encode(ref encoder);
            if (request.Fields.TryGetValue(RequestFieldKey.Context, out OutgoingFieldValue requestField))
            {
                if (requestField.EncodeAction is null)
                {
                    encoder.WriteByteSequence(requestField.ByteSequence);
                }
                else
                {
                    requestField.EncodeAction(ref encoder);
                }
            }
            else
            {
                encoder.EncodeSize(0);
            }
            new EncapsulationHeader(
                encapsulationSize: payloadSize + 6,
                encodingMajor,
                encodingMinor).Encode(ref encoder);

            int frameSize = checked(encoder.EncodedByteCount + payloadSize);
            SliceEncoder.EncodeInt32(frameSize, sizePlaceholder);
        }
    }

    public void OnAbort(Action<Exception> callback) => _onAbort = callback;

    public void OnShutdown(Action<string> callback) => _onShutdown = callback;

    public async Task ShutdownAsync(string message, CancellationToken cancel)
    {
        lock (_mutex)
        {
            _shutdownTask ??= ShutdownAsyncCore(message, _shutdownCancelSource.Token);
        }

        try
        {
            await _shutdownTask.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new ConnectionAbortedException("shutdown canceled");
        }
    }

    internal static IProtocolConnection Create(
        ISimpleNetworkConnection networkConnection,
        bool isServer,
        ConnectionOptions options) =>
        // Dispose objects before losing scope, the icerpc protocol connection is disposed by the decorator.
#pragma warning disable CA2000
        new SynchronizedProtocolConnectionDecorator(
            new IceProtocolConnection(networkConnection, isServer, options),
            options.ConnectTimeout,
            options.ShutdownTimeout);
#pragma warning restore CA2000

    private IceProtocolConnection(
        ISimpleNetworkConnection simpleNetworkConnection,
        bool isServer,
        ConnectionOptions options)
    {
        _dispatcher = options.Dispatcher;
        _maxFrameSize = options.MaxIceFrameSize;
        _idleTimeout = options.IdleTimeout;
        _isServer = isServer;

        if (options.MaxIceConcurrentDispatches > 0)
        {
            _dispatchSemaphore = new AsyncSemaphore(
                initialCount: options.MaxIceConcurrentDispatches,
                maxCount: options.MaxIceConcurrentDispatches);
        }

        // TODO: get the pool and minimum segment size from an option class, but which one? The Slic connection gets
        // these from SlicOptions but another option could be to add Pool/MinimumSegmentSize on
        // ConnectionOptions/ServerOptions. These properties would be used by:
        // - the multiplexed transport implementations
        // - the Ice protocol connection
        _memoryPool = MemoryPool<byte>.Shared;
        _minimumSegmentSize = 4096;

        _networkConnection = simpleNetworkConnection;
        _networkConnectionWriter = new SimpleNetworkConnectionWriter(
            simpleNetworkConnection,
            _memoryPool,
            _minimumSegmentSize);
        _networkConnectionReader = new SimpleNetworkConnectionReader(
            simpleNetworkConnection,
            _memoryPool,
            _minimumSegmentSize,
            abortAction: exception => InvokeOnAbort(exception),
            keepAliveAction: () =>
            {
                lock (_mutex)
                {
                    if (_pingTask.IsCompleted && !_disposeCancelSource.IsCancellationRequested)
                    {
                        _pingTask = PingAsync();
                    }
                }
            });

        _payloadWriter = new IcePayloadPipeWriter(_networkConnectionWriter);

        async Task PingAsync()
        {
            // Make sure we execute the function without holding the connection mutex lock.
            await Task.Yield();

            await _writeSemaphore.EnterAsync(_disposeCancelSource.Token).ConfigureAwait(false);
            try
            {
                EncodeValidateConnectionFrame(_networkConnectionWriter);
                // The flush can't be canceled because it would lead to the writing of an incomplete frame.
                await _networkConnectionWriter.FlushAsync(_disposeCancelSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Connection disposed.
            }
            catch (Exception exception)
            {
                InvokeOnAbort(exception);
            }
            finally
            {
                _writeSemaphore.Release();
            }

            static void EncodeValidateConnectionFrame(SimpleNetworkConnectionWriter writer)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
                IceDefinitions.ValidateConnectionFrame.Encode(ref encoder);
            }
        }
    }

    /// <summary>Creates a pipe reader to simplify the reading of a request or response frame. The frame is read
    /// fully and buffered into an internal pipe.</summary>
    private static async ValueTask<PipeReader> CreateFrameReaderAsync(
        int size,
        SimpleNetworkConnectionReader networkConnectionReader,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        CancellationToken cancel)
    {
        var pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            writerScheduler: PipeScheduler.Inline));

        try
        {
            await networkConnectionReader.FillBufferWriterAsync(
                pipe.Writer,
                size,
                cancel).ConfigureAwait(false);
        }
        catch
        {
            await pipe.Reader.CompleteAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            await pipe.Writer.CompleteAsync().ConfigureAwait(false);
        }

        return pipe.Reader;
    }

    /// <summary>Reads the full Ice payload from the given pipe reader.</summary>
    private static async ValueTask<ReadOnlySequence<byte>> ReadFullPayloadAsync(
        PipeReader payload,
        CancellationToken cancel)
    {
        // We use ReadAtLeastAsync instead of ReadAsync to bypass the PauseWriterThreshold when the payload is
        // backed by a Pipe.
        ReadResult readResult = await payload.ReadAtLeastAsync(int.MaxValue, cancel).ConfigureAwait(false);

        readResult.ThrowIfCanceled(Protocol.Ice);

        return readResult.IsCompleted ? readResult.Buffer :
            throw new ArgumentException("the payload size is greater than int.MaxValue", nameof(payload));
    }

    /// <summary>Notifies the <see cref="OnAbort"/> callback of the connection failure.</summary>
    /// <param name="exception">The cause of the failure.</param>
    private void InvokeOnAbort(Exception exception)
    {
        Action<Exception>? onAbort;
        lock (_mutex)
        {
            onAbort = _onAbort;
            _onAbort = null;
        }
        onAbort?.Invoke(exception);
    }

    /// <summary>Notifies the <see cref="OnShutdown"/> callback of the connection shutdown.</summary>
    /// <param name="message">The shutdown message.</param>
    private void InvokeOnShutdown(string message)
    {
        Action<string>? onShutdown;
        lock (_mutex)
        {
            onShutdown = _onShutdown;
            _onShutdown = null;
        }
        onShutdown?.Invoke(message);
    }

    /// <summary>Read incoming frames and returns on graceful connection shutdown.</summary>
    /// <param name="connection">The connection assigned to <see cref="IncomingFrame.Connection"/>.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    private async ValueTask ReadFramesAsync(IConnection connection, CancellationToken cancel)
    {
        while (true)
        {
            ReadOnlySequence<byte> buffer = await _networkConnectionReader.ReadAtLeastAsync(
                IceDefinitions.PrologueSize,
                cancel).ConfigureAwait(false);

            // First decode and check the prologue.

            ReadOnlySequence<byte> prologueBuffer = buffer.Slice(0, IceDefinitions.PrologueSize);

            IcePrologue prologue = SliceEncoding.Slice1.DecodeBuffer(
                prologueBuffer,
                (ref SliceDecoder decoder) => new IcePrologue(ref decoder));

            _networkConnectionReader.AdvanceTo(prologueBuffer.End);

            IceDefinitions.CheckPrologue(prologue);
            if (prologue.FrameSize > _maxFrameSize)
            {
                throw new InvalidDataException(
                    $"received frame with size ({prologue.FrameSize}) greater than max frame size");
            }

            if (prologue.CompressionStatus == 2)
            {
                throw new NotSupportedException("cannot decompress Ice frame");
            }

            // Then process the frame based on its type.
            switch (prologue.FrameType)
            {
                case IceFrameType.CloseConnection:
                {
                    if (prologue.FrameSize != IceDefinitions.PrologueSize)
                    {
                        throw new InvalidDataException(
                            $"unexpected data for {nameof(IceFrameType.CloseConnection)}");
                    }
                    // The peer expects the connection to be closed as soon as the CloseConnection message is received.
                    _networkConnection.Dispose();
                    return;
                }

                case IceFrameType.Request:
                    await ReadRequestAsync(prologue.FrameSize).ConfigureAwait(false);
                    break;

                case IceFrameType.RequestBatch:
                    // Read and ignore
                    PipeReader batchRequestReader = await CreateFrameReaderAsync(
                        prologue.FrameSize - IceDefinitions.PrologueSize,
                        _networkConnectionReader,
                        _memoryPool,
                        _minimumSegmentSize,
                        cancel).ConfigureAwait(false);
                    await batchRequestReader.CompleteAsync().ConfigureAwait(false);
                    break;

                case IceFrameType.Reply:
                    await ReadReplyAsync(prologue.FrameSize).ConfigureAwait(false);
                    break;

                case IceFrameType.ValidateConnection:
                {
                    // Notify the control stream of the reception of a Ping frame.
                    if (prologue.FrameSize != IceDefinitions.PrologueSize)
                    {
                        throw new InvalidDataException(
                            $"unexpected data for {nameof(IceFrameType.ValidateConnection)}");
                    }
                    break;
                }

                default:
                {
                    throw new InvalidDataException(
                        $"received Ice frame with unknown frame type '{prologue.FrameType}'");
                }
            }
        } // while

        async Task ReadReplyAsync(int replyFrameSize)
        {
            // Read the remainder of the frame immediately into frameReader.
            PipeReader replyFrameReader = await CreateFrameReaderAsync(
                replyFrameSize - IceDefinitions.PrologueSize,
                _networkConnectionReader,
                _memoryPool,
                _minimumSegmentSize,
                cancel).ConfigureAwait(false);

            bool cleanupFrameReader = true;

            try
            {
                // Read and decode request ID
                if (!replyFrameReader.TryRead(out ReadResult readResult) || readResult.Buffer.Length < 4)
                {
                    throw new ConnectionLostException();
                }

                ReadOnlySequence<byte> requestIdBuffer = readResult.Buffer.Slice(0, 4);
                int requestId = SliceEncoding.Slice1.DecodeBuffer(
                    requestIdBuffer,
                    (ref SliceDecoder decoder) => decoder.DecodeInt32());
                replyFrameReader.AdvanceTo(requestIdBuffer.End);

                lock (_mutex)
                {
                    if (_invocations.TryGetValue(
                        requestId,
                        out TaskCompletionSource<PipeReader>? responseCompletionSource))
                    {
                        responseCompletionSource.SetResult(replyFrameReader);

                        cleanupFrameReader = false;
                    }
                    else if (_shutdownTask is null)
                    {
                        throw new InvalidDataException("received ice Reply for unknown invocation");
                    }
                }
            }
            finally
            {
                if (cleanupFrameReader)
                {
                    await replyFrameReader.CompleteAsync().ConfigureAwait(false);
                }
            }
        }

        async Task ReadRequestAsync(int requestFrameSize)
        {
            // Read the request frame.
            PipeReader requestFrameReader = await CreateFrameReaderAsync(
                requestFrameSize - IceDefinitions.PrologueSize,
                _networkConnectionReader,
                _memoryPool,
                _minimumSegmentSize,
                cancel).ConfigureAwait(false);

            // Decode its header.
            int requestId;
            IceRequestHeader requestHeader;
            PipeReader? contextReader;
            try
            {
                if (!requestFrameReader.TryRead(out ReadResult readResult))
                {
                    throw new InvalidDataException("received invalid request frame");
                }

                Debug.Assert(readResult.IsCompleted);

                (requestId, requestHeader, contextReader, int consumed) = DecodeRequestIdAndHeader(readResult.Buffer);
                requestFrameReader.AdvanceTo(readResult.Buffer.GetPosition(consumed));
            }
            catch
            {
                await requestFrameReader.CompleteAsync().ConfigureAwait(false);
                throw;
            }

            IDictionary<RequestFieldKey, ReadOnlySequence<byte>>? fields;
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

                if (requestHeader.OperationMode == OperationMode.Idempotent)
                {
                    fields[RequestFieldKey.Idempotent] = default;
                }
            }

            var request = new IncomingRequest(connection)
            {
                Fields = fields,
                Fragment = requestHeader.Fragment,
                IsOneway = requestId == 0,
                Operation = requestHeader.Operation,
                Path = requestHeader.Path,
                Payload = requestFrameReader,
            };

            CancellationTokenSource? dispatchCancelSource = null;
            bool isClosed = false;
            lock (_mutex)
            {
                if (_shutdownTask is not null)
                {
                    isClosed = true;
                }
                else
                {
                    if (_invocations.Count == 0 && _dispatches.Count == 0)
                    {
                        // Disable idle check
                        _idleTimeoutTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    }

                    dispatchCancelSource = new();
                    _dispatches.Add(dispatchCancelSource);
                }
            }

            if (isClosed)
            {
                // If shutting down or aborted, ignore the incoming request.
                // TODO: replace with payload exception and error code
                await request.Payload.CompleteAsync(new ConnectionClosedException()).ConfigureAwait(false);
                if (contextReader is not null)
                {
                    await contextReader.CompleteAsync().ConfigureAwait(false);

                    // The field values are now invalid - they point to potentially recycled and reused memory. We
                    // replace Fields by an empty dictionary to prevent accidental access to this reused memory.
                    request.Fields = ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty;
                }
            }
            else
            {
                Debug.Assert(dispatchCancelSource is not null);

                if (_dispatchSemaphore is AsyncSemaphore dispatchSemaphore)
                {
                    // This prevents us from receiving any frame until WaitAsync returns.
                    try
                    {
                        await dispatchSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                    }
                    catch
                    {
                        // The semaphore acquisition is canceled on DisposeAsync.
                        Debug.Assert(_disposeCancelSource.IsCancellationRequested);

                        // Cleanup the dispatch.
                        lock (_mutex)
                        {
                            if (_dispatches.Remove(dispatchCancelSource))
                            {
                                if (_invocations.Count == 0 && _dispatches.Count == 0)
                                {
                                    _dispatchesAndInvocationsCompleted.TrySetResult();
                                }
                            }
                        }
                        await request.Payload.CompleteAsync(new ConnectionAbortedException()).ConfigureAwait(false);
                        throw;
                    }
                }

                // The scheduling of the task can't be canceled since we want to make sure DispatchRequestAsync will
                // cleanup the dispatch if DisposeAsync is called.
                _ = Task.Run(
                    () => DispatchRequestAsync(request, contextReader, dispatchCancelSource),
                    CancellationToken.None);
            }

            async Task DispatchRequestAsync(
                IncomingRequest request,
                PipeReader? contextReader,
                CancellationTokenSource dispatchCancelSource)
            {
                using CancellationTokenSource _ = dispatchCancelSource;

                OutgoingResponse response;
                Exception? completeException = null;
                try
                {
                    // The dispatcher can complete the incoming request payload to release its memory as soon as
                    // possible.
                    response = await _dispatcher.DispatchAsync(
                        request,
                        dispatchCancelSource.Token).ConfigureAwait(false);

                    if (response != request.Response)
                    {
                        throw new InvalidOperationException(
                            "the dispatcher did not return the last response created for this request");
                    }
                }
                catch (OperationCanceledException) when (_disposeCancelSource.IsCancellationRequested)
                {
                    response = new OutgoingResponse(request);
                }
                catch (Exception exception)
                {
                    // If we catch an exception, we return a failure response with a Slice-encoded payload.

                    if (exception is not DispatchException dispatchException ||
                        dispatchException.ConvertToUnhandled)
                    {
                        dispatchException = exception is OperationCanceledException ?
                            new DispatchException("dispatch canceled by peer", DispatchErrorCode.Canceled) :
                            new DispatchException(
                                message: null,
                                exception is InvalidDataException ?
                                    DispatchErrorCode.InvalidData : DispatchErrorCode.UnhandledException,
                                exception);
                    }

                    response = new OutgoingResponse(request)
                    {
                        Payload = CreateExceptionPayload(dispatchException, request),
                        ResultType = ResultType.Failure
                    };

                    static PipeReader CreateExceptionPayload(
                        DispatchException dispatchException,
                        IncomingRequest request)
                    {
                        ISliceEncodeFeature encodeFeature = request.Features.Get<ISliceEncodeFeature>() ??
                            SliceEncodeFeature.Default;

                        var pipe = new Pipe(encodeFeature.PipeOptions);

                        var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice1);
                        encoder.EncodeSystemException(
                            dispatchException,
                            request.Path,
                            request.Fragment,
                            request.Operation);
                        pipe.Writer.Complete(); // flush to reader and sets Is[Writer]Completed to true.
                        return pipe.Reader;
                    }
                }
                finally
                {
                    await request.Payload.CompleteAsync(completeException).ConfigureAwait(false);
                    if (contextReader is not null)
                    {
                        await contextReader.CompleteAsync(completeException).ConfigureAwait(false);

                        // The field values are now invalid - they point to potentially recycled and reused memory. We
                        // replace Fields by an empty dictionary to prevent accidental access to this reused memory.
                        request.Fields = ImmutableDictionary<RequestFieldKey, ReadOnlySequence<byte>>.Empty;
                    }
                }

                PipeWriter payloadWriter = _payloadWriter;
                bool acquiredSemaphore = false;

                try
                {
                    _disposeCancelSource.Token.ThrowIfCancellationRequested();

                    if (response.PayloadStream is not null)
                    {
                        throw new NotSupportedException("PayloadStream must be null with the ice protocol");
                    }

                    if (request.IsOneway)
                    {
                        return;
                    }

                    // Read the full payload. This can take some time so this needs to be done before acquiring the
                    // write semaphore.
                    ReadOnlySequence<byte> payload = await ReadFullPayloadAsync(
                        response.Payload,
                        cancel).ConfigureAwait(false);
                    int payloadSize = checked((int)payload.Length);

                    // Wait for writing of other frames to complete. The semaphore is used as an asynchronous queue
                    // to serialize the writing of frames.
                    await _writeSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                    acquiredSemaphore = true;

                    ReplyStatus replyStatus = ReplyStatus.OK;

                    if (response.ResultType != ResultType.Success)
                    {
                        if (response.ResultType == ResultType.Failure)
                        {
                            replyStatus = ((int)payload.FirstSpan[0]).AsReplyStatus();

                            if (replyStatus <= ReplyStatus.UserException)
                            {
                                throw new InvalidDataException(
                                    $"unexpected reply status value '{replyStatus}' in payload");
                            }
                        }
                        else
                        {
                            replyStatus = ReplyStatus.UserException;
                        }
                    }

                    EncodeResponseHeader(_networkConnectionWriter, requestId, payloadSize, replyStatus);

                    payloadWriter = response.GetPayloadWriter(payloadWriter);

                    // Write the payload and complete the source.
                    FlushResult flushResult = await payloadWriter.WriteAsync(
                        payload,
                        endStream: false,
                        cancel).ConfigureAwait(false);

                    // If a payload writer decorator returns a canceled or completed flush result, we have to throw
                    // NotSupportedException. We can't interrupt the writing of a payload since it would lead to a
                    // bogus payload to be sent over the connection.
                    if (flushResult.IsCanceled || flushResult.IsCompleted)
                    {
                        throw new NotSupportedException(
                            "payload writer cancellation or completion is not supported with the ice protocol");
                    }
                }
                catch (OperationCanceledException) when (_disposeCancelSource.IsCancellationRequested)
                {
                    completeException = new ConnectionAbortedException();
                }
                catch (Exception exception)
                {
                    completeException = exception;
                }
                finally
                {
                    request.Complete(completeException);
                    await payloadWriter.CompleteAsync(completeException).ConfigureAwait(false);

                    if (acquiredSemaphore)
                    {
                        _writeSemaphore.Release();
                    }

                    lock (_mutex)
                    {
                        _dispatchSemaphore?.Release();

                        // Dispatch is done, remove the cancellation token source for the dispatch.
                        if (_dispatches.Remove(dispatchCancelSource))
                        {
                            if (_invocations.Count == 0 && _dispatches.Count == 0)
                            {
                                if (_shutdownTask is not null)
                                {
                                    _dispatchesAndInvocationsCompleted.TrySetResult();
                                }
                                else if (!_disposeCancelSource.IsCancellationRequested)
                                {
                                    _idleTimeoutTimer?.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                                }
                            }
                        }
                    }
                }

                static void EncodeResponseHeader(
                    SimpleNetworkConnectionWriter writer,
                    int requestId,
                    int payloadSize,
                    ReplyStatus replyStatus)
                {
                    var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);

                    // Write the response header.

                    encoder.WriteByteSpan(IceDefinitions.FramePrologue);
                    encoder.EncodeIceFrameType(IceFrameType.Reply);
                    encoder.EncodeUInt8(0); // compression status
                    Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);

                    encoder.EncodeInt32(requestId);

                    if (replyStatus <= ReplyStatus.UserException)
                    {
                        encoder.EncodeReplyStatus(replyStatus);

                        // When IceRPC receives a response, it ignores the response encoding. So this "1.1" is only
                        // relevant to a ZeroC Ice client that decodes the response. The only Slice encoding such a
                        // client can possibly use to decode the response payload is 1.1 or 1.0, and we don't care
                        // about interop with 1.0.
                        var encapsulationHeader = new EncapsulationHeader(
                            encapsulationSize: payloadSize + 6,
                            payloadEncodingMajor: 1,
                            payloadEncodingMinor: 1);
                        encapsulationHeader.Encode(ref encoder);
                    }
                    // else the reply status (> UserException) is part of the payload

                    int frameSize = encoder.EncodedByteCount + payloadSize;
                    SliceEncoder.EncodeInt32(frameSize, sizePlaceholder);
                }
            }

            static (int RequestId, IceRequestHeader Header, PipeReader? ContextReader, int Consumed) DecodeRequestIdAndHeader(
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
                        @$"unsupported payload encoding '{encapsulationHeader.PayloadEncodingMajor
                        }.{encapsulationHeader.PayloadEncodingMinor}'");
                }

                int payloadSize = encapsulationHeader.EncapsulationSize - 6;
                if (payloadSize != (buffer.Length - decoder.Consumed))
                {
                    throw new InvalidDataException(
                        @$"request payload size mismatch: expected {payloadSize
                        } bytes, read {buffer.Length - decoder.Consumed} bytes");
                }

                return (requestId, requestHeader, contextPipe?.Reader, (int)decoder.Consumed);
            }
        }
    }

    private async Task ShutdownAsyncCore(string message, CancellationToken cancel)
    {
        // Make sure we execute the function without holding the connection mutex lock.
        await Task.Yield();

        InvokeOnShutdown(message);

        IEnumerable<TaskCompletionSource<PipeReader>> invocations;
        lock (_mutex)
        {
            if (_dispatches.Count == 0 && _invocations.Count == 0)
            {
                _dispatchesAndInvocationsCompleted.TrySetResult();
            }
            invocations = _invocations.Values.ToArray();
        }

        // Wait for dispatches and invocations to complete.
        await _dispatchesAndInvocationsCompleted.Task.WaitAsync(cancel).ConfigureAwait(false);

        // Encode and write the CloseConnection frame once all the dispatches are done.
        try
        {
            await _writeSemaphore.EnterAsync(cancel).ConfigureAwait(false);
            try
            {
                EncodeCloseConnectionFrame(_networkConnectionWriter);
                await _networkConnectionWriter.FlushAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                _writeSemaphore.Release();
            }

            // When the peer receives the CloseConnection frame, the peer closes the connection. We wait for the connection
            // closure here. We can't just return and close the underlying transport since this could abort the receive of
            // the dispatch responses and close connection frame by the peer.
            await _pendingClose.Task.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch
        {
            // Ignore, expected if the connection is closed by the peer first.
        }

        static void EncodeCloseConnectionFrame(SimpleNetworkConnectionWriter writer)
        {
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice1);
            IceDefinitions.CloseConnectionFrame.Encode(ref encoder);
        }
    }
}
