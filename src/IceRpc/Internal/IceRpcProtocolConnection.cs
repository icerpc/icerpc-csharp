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
    internal override ServerAddress ServerAddress => _transportConnection.ServerAddress;

    private Exception? _invocationCanceledException;
    private Task? _acceptRequestsTask;
    private IConnectionContext? _connectionContext; // non-null once the connection is established
    private IMultiplexedStream? _controlStream;
    private int _dispatchCount;
    private readonly IDispatcher? _dispatcher;
    private readonly CancellationTokenSource _dispatchesAndInvocationsCancelSource = new();
    private readonly TaskCompletionSource _dispatchesCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // The number of bytes we need to encode a size up to _maxRemoteHeaderSize. It's 2 for DefaultMaxHeaderSize.
    private int _headerSizeLength = 2;
    private bool _isReadOnly;
    private long _lastRemoteBidirectionalStreamId = -1;
    // TODO: to we really need to keep track of this since we don't keep track of one-way requests?
    private long _lastRemoteUnidirectionalStreamId = -1;
    private readonly int _maxLocalHeaderSize;
    private int _maxRemoteHeaderSize = ConnectionOptions.DefaultMaxIceRpcHeaderSize;
    private readonly object _mutex = new();
    private readonly IMultiplexedConnection _transportConnection;
    private Task<IceRpcGoAway>? _readGoAwayTask;
    private IMultiplexedStream? _remoteControlStream;

    private readonly HashSet<IMultiplexedStream> _streams = new();
    private readonly TaskCompletionSource _streamsCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly CancellationTokenSource _tasksCancelSource = new();
    private Task? _waitForConnectionFailure;

    internal IceRpcProtocolConnection(
        IMultiplexedConnection transportConnection,
        IProtocolConnectionObserver? observer,
        ConnectionOptions options)
        : base(observer, options)
    {
        _transportConnection = transportConnection;
        _dispatcher = options.Dispatcher;
        _maxLocalHeaderSize = options.MaxIceRpcHeaderSize;
    }

    private protected override void CancelDispatchesAndInvocations(Exception exception)
    {
        lock (_mutex)
        {
            if (_invocationCanceledException is not null)
            {
                return;
            }

            _isReadOnly = true; // prevent new dispatches or invocations from being accepted.

            // Set the abort exception for invocations.
            _invocationCanceledException = exception;

            if (_streams.Count == 0)
            {
                _streamsCompleted.TrySetResult();
            }
            if (_dispatchCount == 0)
            {
                _dispatchesCompleted.TrySetResult();
            }
        }

        _dispatchesAndInvocationsCancelSource.Cancel();
    }

    private protected override bool CheckIfIdle()
    {
        lock (_mutex)
        {
            // If idle, mark the connection as readonly to stop accepting new dispatches or invocations.
            if (_streams.Count == 0)
            {
                _isReadOnly = true;
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    private protected override async Task<TransportConnectionInformation> ConnectAsyncCore(CancellationToken cancel)
    {
        // Connect the transport connection
        TransportConnectionInformation transportConnectionInformation = await _transportConnection.ConnectAsync(cancel)
            .ConfigureAwait(false);

        ServerEventSource.Log.ConnectionStart(Protocol.Ice, transportConnectionInformation);
        OnAbort(exception =>
            ServerEventSource.Log.ConnectionFailure(Protocol.Ice, transportConnectionInformation, exception));
        OnDispose(() => ServerEventSource.Log.ConnectionStop(Protocol.Ice, transportConnectionInformation));

        // This needs to be set before starting the accept requests task bellow.
        _connectionContext = new ConnectionContext(this, transportConnectionInformation);

        _controlStream = _transportConnection.CreateStream(false);

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
            endStream: false,
            cancel).ConfigureAwait(false);

        // Wait for the remote control stream to be accepted and read the protocol Settings frame
        _remoteControlStream = await _transportConnection.AcceptStreamAsync(cancel).ConfigureAwait(false);

        await ReceiveControlFrameHeaderAsync(IceRpcControlFrameType.Settings, cancel).ConfigureAwait(false);
        await ReceiveSettingsFrameBody(cancel).ConfigureAwait(false);

        // Start a task to read the go away frame from the control stream and initiate shutdown.
        _readGoAwayTask = Task.Run(
            async () =>
            {
                CancellationToken cancel = _tasksCancelSource.Token;
                await ReceiveControlFrameHeaderAsync(IceRpcControlFrameType.GoAway, cancel).ConfigureAwait(false);
                IceRpcGoAway goAwayFrame = await ReceiveGoAwayBodyAsync(cancel).ConfigureAwait(false);

                InitiateShutdown(goAwayFrame.Message);
                return goAwayFrame;
            },
            CancellationToken.None);

        if (_dispatcher is not null)
        {
            // Start a task to start accepting requests.
            _acceptRequestsTask = Task.Run(
                async () =>
                {
                    try
                    {
                        while (true)
                        {
                            IMultiplexedStream stream = await _transportConnection.AcceptStreamAsync(
                                _tasksCancelSource.Token).ConfigureAwait(false);

                            try
                            {
                                await AcceptRequestAsync(stream, _tasksCancelSource.Token).ConfigureAwait(false);
                            }
                            catch (IceRpcProtocolStreamException)
                            {
                                // A stream failure is not a fatal connection error; we can continue accepting new
                                // requests.
                            }
                        }
                    }
                    catch
                    {
                        // Ignore connection failures here, this is handled by the _waitForConnectionFailure task.
                    }
                },
                CancellationToken.None);
        }

        _waitForConnectionFailure = Task.Run(
            async () =>
            {
                try
                {
                    await _remoteControlStream!.ReadsClosed.WaitAsync(_tasksCancelSource.Token).ConfigureAwait(false);
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
                    }

                    // Otherwise, it's an unexpected connection failure.

                    var connectionLostException = new ConnectionLostException(exception);

                    InvokeOnAbort(connectionLostException);

                    // Don't wait for DisposeAsync to be called to cancel dispatches and invocations which might still
                    // be running.
                    CancelDispatchesAndInvocations(connectionLostException);
                }
            },
            CancellationToken.None);

        return transportConnectionInformation;
    }

    private protected override async ValueTask DisposeAsyncCore()
    {
        // Before disposing the transport connection, cancel pending tasks which are using the transport connection and
        // wait for the tasks to complete.
        _tasksCancelSource.Cancel();
        try
        {
            await Task.WhenAll(
                _acceptRequestsTask ?? Task.CompletedTask,
                _readGoAwayTask ?? Task.CompletedTask,
                _waitForConnectionFailure ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch
        {
            // Ignore, we don't care if the tasks fail here (ReadGoAwayTask can fail if the connection is lost).
        }

        // Cancel dispatches and invocations.
        CancelDispatchesAndInvocations(new ConnectionAbortedException("connection disposed"));

        // Dispose the transport connection to kill the connection with the peer.
        await _transportConnection.DisposeAsync().ConfigureAwait(false);

        // Next, wait for dispatches and invocations to complete.
        await Task.WhenAll(_dispatchesCompleted.Task, _streamsCompleted.Task).ConfigureAwait(false);

        _tasksCancelSource.Dispose();
        _dispatchesAndInvocationsCancelSource.Dispose();
    }

    private protected override async Task<IncomingResponse> InvokeAsyncCore(
        OutgoingRequest request,
        CancellationToken cancel)
    {
        IMultiplexedStream? stream = null;
        CancellationTokenRegistration? abortTokenRegistration = null;
        Exception? completeException = null;

        try
        {
            if (request.ServiceAddress.Fragment.Length > 0)
            {
                throw new NotSupportedException("the icerpc protocol does not support fragments");
            }

            // Create the stream.
            stream = _transportConnection.CreateStream(bidirectional: !request.IsOneway);

            // Abort the stream if the invocation is canceled.
            abortTokenRegistration = cancel.UnsafeRegister(
                stream => ((IMultiplexedStream)stream!).Abort(new OperationCanceledException("invocation canceled")),
                stream);

            // Keep track of the invocation for the shutdown logic.
            if (!request.IsOneway || request.PayloadStream is not null)
            {
                lock (_mutex)
                {
                    if (_isReadOnly)
                    {
                        // Don't process the invocation if the connection is in the process of shutting down or it's
                        // already closed.
                        throw new ConnectionClosedException();
                    }
                    else
                    {
                        if (_streams.Count == 0)
                        {
                            DisableIdleCheck();
                        }
                        _streams.Add(stream);

                        _ = RemoveStreamOnWritesAndReadsClosedAsync(stream);
                    }
                }
            }

            EncodeHeader(stream.Output);

            // SendPayloadAsync takes care of the completion of the stream output.
            await SendPayloadAsync(request, stream, _dispatchesAndInvocationsCancelSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_invocationCanceledException is not null)
        {
            completeException = _invocationCanceledException;
            throw completeException;
        }
        catch (Exception exception)
        {
            completeException = exception;
            throw;
        }
        finally
        {
            if (completeException is not null)
            {
                if (stream is not null)
                {
                    await stream.Output.CompleteAsync(completeException).ConfigureAwait(false);
                    if (stream.IsBidirectional)
                    {
                        await stream.Input.CompleteAsync(completeException).ConfigureAwait(false);
                    }
                }

                if (abortTokenRegistration is not null)
                {
                    await abortTokenRegistration.Value.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        try
        {
            if (request.IsOneway)
            {
                return new IncomingResponse(request, _connectionContext!);
            }

            ReadResult readResult = await stream.Input.ReadSegmentAsync(
                SliceEncoding.Slice2,
                _maxLocalHeaderSize,
                _dispatchesAndInvocationsCancelSource.Token).ConfigureAwait(false);

            // Nothing cancels the stream input pipe reader.
            Debug.Assert(!readResult.IsCanceled);

            if (readResult.Buffer.IsEmpty)
            {
                throw new InvalidDataException($"received icerpc response with empty header");
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
        catch (OperationCanceledException) when (_invocationCanceledException is not null)
        {
            completeException = _invocationCanceledException;
            throw completeException;
        }
        catch (Exception exception)
        {
            completeException = exception;
            throw;
        }
        finally
        {
            if (completeException is not null)
            {
                Debug.Assert(!request.IsOneway);
                await stream.Input.CompleteAsync(completeException).ConfigureAwait(false);
            }

            if (abortTokenRegistration is not null)
            {
                await abortTokenRegistration.Value.DisposeAsync().ConfigureAwait(false);
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

    private protected override async Task ShutdownAsyncCore(string message, CancellationToken cancel)
    {
        IceRpcGoAway goAwayFrame;
        lock (_mutex)
        {
            _isReadOnly = true;
            if (_streams.Count == 0)
            {
                _streamsCompleted.TrySetResult();
            }
            if (_dispatchCount == 0)
            {
                _dispatchesCompleted.TrySetResult();
            }
            goAwayFrame = new(_lastRemoteBidirectionalStreamId, _lastRemoteUnidirectionalStreamId, message);
        }

        await SendControlFrameAsync(
            IceRpcControlFrameType.GoAway,
            (ref SliceEncoder encoder) => goAwayFrame.Encode(ref encoder),
            endStream: true,
            cancel).ConfigureAwait(false);

        // Wait for the peer to send back a GoAway frame. The task should already be completed if the shutdown has been
        // initiated by the peer.
        IceRpcGoAway peerGoAwayFrame = await _readGoAwayTask!.WaitAsync(cancel).ConfigureAwait(false);

        // Abort streams for invocations that were not dispatched by the peer. The invocations will throw
        // ConnectionClosedException which can be retried.
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

        var closedException = new ConnectionClosedException(message);
        foreach (IMultiplexedStream stream in invocations)
        {
            stream.Abort(closedException);
        }

        // Wait for dispatches and streams to complete and shutdown the connection.
        await Task.WhenAll(
            _dispatchesCompleted.Task,
            _streamsCompleted.Task).WaitAsync(cancel).ConfigureAwait(false);

        // Shutdown the transport and wait for the peer shutdown.
        await _transportConnection.ShutdownAsync(closedException, cancel).ConfigureAwait(false);
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
        CancellationToken cancel)
    {
        PipeWriter payloadWriter = outgoingFrame.GetPayloadWriter(stream.Output);
        PipeReader? payloadStream = outgoingFrame.PayloadStream;

        try
        {
            FlushResult flushResult = await CopyReaderToWriterAsync(
                outgoingFrame.Payload,
                payloadWriter,
                endStream: payloadStream is null,
                cancel).ConfigureAwait(false);

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
                cancel);
        }

        async Task<FlushResult> CopyReaderToWriterAsync(
            PipeReader reader,
            PipeWriter writer,
            bool endStream,
            CancellationToken cancel)
        {
            // If the peer is no longer interested to receive the payload, cancel the reading of the payload.
            _ = CancelPendingReadOnWritesClosedAsync();

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

                readResult.ThrowIfCanceled(Protocol.IceRpc);

                if (readResult.IsCompleted)
                {
                    // We're done if there's no more data to send for the payload.
                    break;
                }
            }
            while (!flushResult.IsCanceled && !flushResult.IsCompleted);
            return flushResult;

            async Task CancelPendingReadOnWritesClosedAsync()
            {
                try
                {
                    await stream.WritesClosed.ConfigureAwait(false);
                }
                catch
                {
                    // Ignore the reason of the writes close.
                }

                reader.CancelPendingRead();
            }
        }
    }

    private async Task AcceptRequestAsync(IMultiplexedStream stream, CancellationToken cancel)
    {
        Debug.Assert(_dispatcher is not null);

        PipeReader? fieldsPipeReader = null;

        try
        {
            ReadResult readResult = await stream.Input.ReadSegmentAsync(
                SliceEncoding.Slice2,
                _maxLocalHeaderSize,
                cancel).ConfigureAwait(false);

            if (readResult.Buffer.IsEmpty)
            {
                throw new InvalidDataException("received icerpc request with empty header");
            }

            lock (_mutex)
            {
                if (_isReadOnly)
                {
                    throw new ConnectionClosedException();
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

                    if (_streams.Count == 0)
                    {
                        DisableIdleCheck();
                    }

                    _streams.Add(stream);

                    _ = RemoveStreamOnWritesAndReadsClosedAsync(stream);
                }
            }

            (IceRpcRequestHeader header, IDictionary<RequestFieldKey, ReadOnlySequence<byte>> fields, fieldsPipeReader) =
                DecodeHeader(readResult.Buffer);
            stream.Input.AdvanceTo(readResult.Buffer.End);

            var request = new IncomingRequest(_connectionContext!)
            {
                Fields = fields,
                IsOneway = !stream.IsBidirectional,
                Operation = header.Operation,
                Path = header.Path,
                Payload = stream.Input
            };

            _ = Task.Run(
                () => DispatchRequestAsync(request, stream, fieldsPipeReader),
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

            throw;
        }

        async Task DispatchRequestAsync(
            IncomingRequest request,
            IMultiplexedStream stream,
            PipeReader? fieldsPipeReader)
        {
            using var dispatchCancelSource = new CancellationTokenSource();

            // If the peer is no longer interested in the response of the dispatch, we cancel the dispatch.
            _ = CancelDispatchOnWritesClosedAsync();

            // Cancel the dispatch cancellation token source if dispatches and invocations are canceled.
            using CancellationTokenRegistration tokenRegistration =
                _dispatchesAndInvocationsCancelSource.Token.UnsafeRegister(
                    cts => ((CancellationTokenSource)cts!).Cancel(),
                    dispatchCancelSource);

            OutgoingResponse response;
            try
            {
                response = await _dispatcher.DispatchAsync(request, dispatchCancelSource.Token).ConfigureAwait(false);

                if (response != request.Response)
                {
                    throw new InvalidOperationException(
                        "the dispatcher did not return the last response created for this request");
                }
            }
            catch (OperationCanceledException exception) when (dispatchCancelSource.IsCancellationRequested)
            {
                await stream.Output.CompleteAsync(exception).ConfigureAwait(false);
                request.Complete();
                return;
            }
            catch (Exception exception)
            {
                // If we catch an exception, we return a failure response with a Slice-encoded payload.

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
                await SendPayloadAsync(response, stream, dispatchCancelSource.Token).ConfigureAwait(false);
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

            async Task CancelDispatchOnWritesClosedAsync()
            {
                try
                {
                    await stream.WritesClosed.ConfigureAwait(false);
                }
                catch
                {
                    // Ignore the reason of the writes close.
                }

                try
                {
                    dispatchCancelSource.Cancel();
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
                @$"header size ({headerSize}) is greater than the remote peer's max header size ({_maxRemoteHeaderSize})");
        }
    }

    private async ValueTask ReceiveControlFrameHeaderAsync(
        IceRpcControlFrameType expectedFrameType,
        CancellationToken cancel)
    {
        PipeReader input = _remoteControlStream!.Input;

        while (true)
        {
            ReadResult readResult = await input.ReadAsync(cancel).ConfigureAwait(false);
            readResult.ThrowIfCanceled(Protocol.IceRpc);

            if (readResult.Buffer.IsEmpty)
            {
                throw new InvalidDataException("invalid empty control frame");
            }

            IceRpcControlFrameType frameType = readResult.Buffer.FirstSpan[0].AsIceRpcControlFrameType();
            input.AdvanceTo(readResult.Buffer.GetPosition(1));

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

    private async ValueTask<IceRpcGoAway> ReceiveGoAwayBodyAsync(CancellationToken cancel)
    {
        PipeReader input = _remoteControlStream!.Input;
        ReadResult readResult = await input.ReadSegmentAsync(
            SliceEncoding.Slice2,
            _maxLocalHeaderSize,
            cancel).ConfigureAwait(false);

        readResult.ThrowIfCanceled(Protocol.IceRpc);

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

    private async ValueTask ReceiveSettingsFrameBody(CancellationToken cancel)
    {
        // We are still in the single-threaded initialization at this point.

        PipeReader input = _remoteControlStream!.Input;
        ReadResult readResult = await input.ReadSegmentAsync(
            SliceEncoding.Slice2,
            _maxLocalHeaderSize,
            cancel).ConfigureAwait(false);

        readResult.ThrowIfCanceled(Protocol.IceRpc);

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

    private async Task RemoveStreamOnWritesAndReadsClosedAsync(IMultiplexedStream stream)
    {
        try
        {
            await Task.WhenAll(stream.ReadsClosed, stream.WritesClosed).ConfigureAwait(false);
        }
        catch
        {
            // Ignore the reason of the reads/writes close.
        }

        lock (_mutex)
        {
            _streams.Remove(stream);

            if (_streams.Count == 0)
            {
                if (_isReadOnly)
                {
                    // If shutting down, we can set the _streamsCompleted task completion source
                    // as completed to allow shutdown to progress.
                    _streamsCompleted.TrySetResult();
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
        EncodeAction? encodeAction,
        bool endStream,
        CancellationToken cancel)
    {
        PipeWriter output = _controlStream!.Output;
        output.GetSpan()[0] = (byte)frameType;
        output.Advance(1);

        if (encodeAction is not null)
        {
            EncodeFrame(output);
        }

        return output.WriteAsync(ReadOnlySequence<byte>.Empty, endStream, cancel); // Flush

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
}
