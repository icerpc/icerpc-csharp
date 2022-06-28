// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001: Types that own disposable fields should be disposable",
    Justification = "the disposable fields are cleaned up by Abort")]
internal sealed class IceRpcProtocolConnection : IProtocolConnection
{
    public Protocol Protocol => Protocol.IceRpc;

    private readonly CancellationTokenSource _abortCancelSource = new();
    private Task? _acceptRequestsTask;
    private IMultiplexedStream? _controlStream;
    private readonly HashSet<CancellationTokenSource> _cancelDispatchSources = new();
    private readonly IDispatcher _dispatcher;

    // The number of bytes we need to encode a size up to _maxRemoteHeaderSize. It's 2 for DefaultMaxHeaderSize.
    private int _headerSizeLength = 2;
    private readonly TimeSpan _idleTimeout;
    private Timer? _idleTimeoutTimer;
    private bool _isOnCloseCalled;
    private long _lastRemoteBidirectionalStreamId = -1;
    // TODO: to we really need to keep track of this since we don't keep track of one-way requests?
    private long _lastRemoteUnidirectionalStreamId = -1;
    private readonly int _maxLocalHeaderSize;
    private int _maxRemoteHeaderSize = ConnectionOptions.DefaultMaxIceRpcHeaderSize;
    private readonly object _mutex = new();
    private readonly IMultiplexedNetworkConnection _networkConnection;
    private Action<Exception>? _onClose;
    private IMultiplexedStream? _remoteControlStream;
    private Task? _shutdownTask;
    private readonly HashSet<IMultiplexedStream> _streams = new();
    private readonly TaskCompletionSource _streamsCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task? _waitForGoAwayTask;

    private readonly TaskCompletionSource<IceRpcGoAway> _waitForGoAwayFrame = new();

    // TODO: XXX remove exception?
    public void Abort(Exception exception)
    {
        Console.Error.WriteLine($"ABORTING FROM ABORT {exception}");
        _abortCancelSource.Cancel();
    }

    public async Task<NetworkConnectionInformation> ConnectAsync(
        bool isServer,
        IConnection connection,
        CancellationToken cancel)
    {
        using CancellationTokenSource _ = CreateLinkedCancellationTokenSource(ref cancel);

        // Connect the network connection
        NetworkConnectionInformation networkConnectionInformation =
            await _networkConnection.ConnectAsync(cancel).ConfigureAwait(false);

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

        if (_idleTimeout != Timeout.InfiniteTimeSpan)
        {
            _idleTimeoutTimer = new Timer(
                _ =>
                {
                    lock (_mutex)
                    {
                        if (_streams.Count > 0)
                        {
                            return; // The connection is no longer idle.
                        }

                        _shutdownTask ??= ShutdownAsyncCore("idle connection", _abortCancelSource.Token);
                    }
                },
                null,
                _idleTimeout,
                Timeout.InfiniteTimeSpan);
        }

        // Start a task to wait to receive the go away frame to initiate shutdown.
        _waitForGoAwayTask = Task.Run(() => WaitForGoAwayAsync(_abortCancelSource.Token), CancellationToken.None);

        // Start a task to start accepting requests.
        _acceptRequestsTask = Task.Run(
            async () =>
            {
                Exception? completeException = null;
                try
                {
                    while (true)
                    {
                        IMultiplexedStream stream = await _networkConnection.AcceptStreamAsync(
                            _abortCancelSource.Token).ConfigureAwait(false);

                        try
                        {
                            await AcceptRequestAsync(
                                stream,
                                connection,
                                _abortCancelSource.Token).ConfigureAwait(false);
                        }
                        catch (IceRpcProtocolStreamException)
                        {
                            // A stream failure is not a fatal connection error; we can continue accepting new requests.
                        }
                    }
                }
                catch (OperationCanceledException) when (_abortCancelSource.IsCancellationRequested)
                {
                    completeException = new ConnectionAbortedException();
                }
                catch (Exception exception)
                {
                    completeException = exception;
                }
                finally
                {
                    InvokeOnClose(completeException ?? new ConnectionClosedException());
                }
            },
            CancellationToken.None);

        return networkConnectionInformation;
    }

    public async ValueTask DisposeAsync()
    {
        // await Console.Error.WriteLineAsync($"DISPOSING {Environment.StackTrace}").ConfigureAwait(false);
        var exception = new ConnectionClosedException();
        InvokeOnClose(exception);

        IEnumerable<IMultiplexedStream> streams;
        lock (_mutex)
        {
            _shutdownTask ??= Task.CompletedTask;
            streams = _streams.ToArray();
        }

        // Abort streams for for pending invocations.
        foreach (IMultiplexedStream stream in streams)
        {
            if (!stream.IsRemote)
            {
                stream.Abort(exception);
            }
        }

        // Cancel pending dispatches.
        foreach (CancellationTokenSource cancelDispatchSource in _cancelDispatchSources.ToArray())
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

        // TODO: wait for dispatch tasks to complete

        // Abort all the streams and wait for them indefinitely to complete.
        foreach (IMultiplexedStream stream in streams)
        {
            stream.Abort(exception);
        }
        await _streamsCompleted.Task.ConfigureAwait(false);

        // Abort the connection and wait for the pending tasks to return.
        await Console.Error.WriteLineAsync($"DIPOSE ABORTING CANCEL SOURCE").ConfigureAwait(false);
        _abortCancelSource.Cancel();

        if (_acceptRequestsTask is not null)
        {
            await _acceptRequestsTask.ConfigureAwait(false);
        }

        if (_waitForGoAwayTask is not null)
        {
            await _waitForGoAwayTask.ConfigureAwait(false);
        }

        if (_shutdownTask is not null)
        {
            try
            {
                await _shutdownTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore
            }
        }

        // No more pending tasks are running, we can safely release the resources.

        if (_controlStream is not null)
        {
            await _controlStream.Output.CompleteAsync(exception).ConfigureAwait(false);
        }

        if (_remoteControlStream is not null)
        {
            await _remoteControlStream.Input.CompleteAsync(exception).ConfigureAwait(false);
        }

        _networkConnection.Dispose();
        _abortCancelSource.Dispose();
        _idleTimeoutTimer?.Dispose();
    }

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
            if (!request.IsOneway || request.PayloadStream is not null)
            {
                lock (_mutex)
                {
                    if (_shutdownTask is not null || _abortCancelSource.IsCancellationRequested)
                    {
                        // Don't process the invocation if the connection is in the process of shutting down or it's
                        // already closed.
                        throw new ConnectionClosedException();
                    }
                    else
                    {
                        if (_streams.Count == 0)
                        {
                            // Disable the idle check.
                            _idleTimeoutTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                        }

                        _streams.Add(stream);

                        stream.OnShutdown(() =>
                        {
                            lock (_mutex)
                            {
                                _streams.Remove(stream);

                                if (_streams.Count == 0)
                                {
                                    if (_shutdownTask is not null)
                                    {
                                        // If shutting down, we can set the _streamsCompleted task completion source
                                        // as completed to allow shutdown to progress.
                                        _streamsCompleted.TrySetResult();
                                    }
                                    else if (!_abortCancelSource.IsCancellationRequested)
                                    {
                                        // Enable the idle check.
                                        _idleTimeoutTimer?.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                                    }
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
            if (exception is OperationCanceledException && _abortCancelSource.Token.IsCancellationRequested)
            {
                exception = new ConnectionAbortedException();
            }

            if (stream is not null)
            {
                await stream.Output.CompleteAsync(exception).ConfigureAwait(false);
                if (stream.IsBidirectional)
                {
                    await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
                }
            }

            // TODO: Should we wrap unexpected exceptions with ConnectionLostException?
            throw;
        }

        if (request.IsOneway)
        {
            return new IncomingResponse(request, connection);
        }

        Debug.Assert(stream is not null);
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
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && _abortCancelSource.Token.IsCancellationRequested)
            {
                exception = new ConnectionAbortedException();
            }

            await stream.Input.CompleteAsync(exception).ConfigureAwait(false);
            // TODO: Should we wrap unexpected exceptions with ConnectionLostException?
            throw;
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

    public void OnClose(Action<Exception> callback)
    {
        bool executeCallback = false;

        lock (_mutex)
        {
            if (_isOnCloseCalled)
            {
                executeCallback = true;
            }
            else
            {
                _onClose += callback;
            }
        }

        if (executeCallback)
        {
            callback(new ConnectionClosedException());
        }
    }

    public async Task ShutdownAsync(string message, CancellationToken cancel)
    {
        using CancellationTokenSource _ = CreateLinkedCancellationTokenSource(ref cancel);
        lock (_mutex)
        {
            _shutdownTask ??= ShutdownAsyncCore(message, cancel);
        }

        try
        {
            await _shutdownTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_abortCancelSource.IsCancellationRequested)
        {
            await Console.Error.WriteLineAsync($"SHUTDOWN ABORTED").ConfigureAwait(false);
            throw new ConnectionAbortedException();
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"SHUTDOWN EXCEPTION {exception}").ConfigureAwait(false);
        }
    }

    internal IceRpcProtocolConnection(IMultiplexedNetworkConnection networkConnection, ConnectionOptions options)
    {
        _networkConnection = networkConnection;
        _dispatcher = options.Dispatcher;
        _idleTimeout = options.IdleTimeout;
        _maxLocalHeaderSize = options.MaxIceRpcHeaderSize;
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

                readResult.ThrowIfCanceled(Protocol.IceRpc);

                if (readResult.IsCompleted)
                {
                    // We're done if there's no more data to send for the payload.
                    break;
                }
            }
            while (!flushResult.IsCanceled && !flushResult.IsCompleted);
            return flushResult;
        }
    }

    private async Task AcceptRequestAsync(IMultiplexedStream stream, IConnection connection, CancellationToken cancel)
    {
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

            CancellationTokenSource? cancelDispatchSource = null;

            lock (_mutex)
            {
                if (_shutdownTask is not null || _abortCancelSource.IsCancellationRequested)
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

                    if (_streams.Count == 0)
                    {
                        // Disable the idle check.
                        _idleTimeoutTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    }

                    _streams.Add(stream);

                    stream.OnShutdown(() =>
                    {
                        lock (_mutex)
                        {
                            _streams.Remove(stream);

                            if (_streams.Count == 0)
                            {
                                if (_shutdownTask is not null)
                                {
                                    // If shutting down, we can set the _streamsCompleted task completion source
                                    // as completed to allow shutdown to progress.
                                    _streamsCompleted.TrySetResult();
                                }
                                else if (!_abortCancelSource.IsCancellationRequested)
                                {
                                    // Enable the idle check.
                                    _idleTimeoutTimer?.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                                }
                            }

                            _cancelDispatchSources.Remove(cancelDispatchSource);
                        }

                        // If the stream is shutdown because the connection is aborted, make sure to cancel
                        // the dispatch.
                        if (_abortCancelSource.IsCancellationRequested)
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

            Debug.Assert(cancelDispatchSource is not null);
            _ = Task.Run(
                () => DispatchRequestAsync(request, stream, fieldsPipeReader, cancelDispatchSource),
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

            if (exception is OperationCanceledException && _abortCancelSource.IsCancellationRequested)
            {
                throw new ConnectionAbortedException();
            }
            else
            {
                throw;
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

            using CancellationTokenSource _ = cancelDispatchSource;
            CancellationToken cancel = cancelDispatchSource.Token;
            using CancellationTokenSource linkedSource = CreateLinkedCancellationTokenSource(ref cancel);

            OutgoingResponse response;
            try
            {
                response = await _dispatcher.DispatchAsync(request, cancel).ConfigureAwait(false);

                if (response != request.Response)
                {
                    throw new InvalidOperationException(
                        "the dispatcher did not return the last response created for this request");
                }
            }
            catch (OperationCanceledException exception)
            {
                await stream.Output.CompleteAsync(exception).ConfigureAwait(false);

                // We're done since the completion of the stream Output pipe writer aborted the stream or the
                // stream was already aborted.
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

    private void InvokeOnClose(Exception exception)
    {
        Action<Exception>? onClose;

        lock (_mutex)
        {
            _isOnCloseCalled = true;
            onClose = _onClose;
            _onClose = null; // clear _onClose because we want to execute it only once
        }

        // Execute callbacks (if any) outside lock
        onClose?.Invoke(exception);
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

    private ValueTask<FlushResult> SendControlFrameAsync(
        IceRpcControlFrameType frameType,
        EncodeAction? encodeAction,
        CancellationToken cancel)
    {
        PipeWriter output = _controlStream!.Output;
        output.GetSpan()[0] = (byte)frameType;
        output.Advance(1);

        if (encodeAction is not null)
        {
            EncodeFrame(output);
        }

        return output.FlushAsync(cancel);

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

    private void CheckRemoteHeaderSize(int headerSize)
    {
        if (headerSize > _maxRemoteHeaderSize)
        {
            throw new ProtocolException(
                @$"header size ({headerSize
                }) is greater than the remote peer's max header size ({_maxRemoteHeaderSize})");
        }
    }

    private CancellationTokenSource CreateLinkedCancellationTokenSource(ref CancellationToken cancel)
    {
        var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        linkedTokenSource.Token.Register(Cancel, _abortCancelSource);
        cancel = linkedTokenSource.Token;
        return linkedTokenSource;

        static void Cancel(object? source)
        {
            if (source is CancellationTokenSource cancellationTokenSource)
            {
                try
                {
                    cancellationTokenSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private async Task ShutdownAsyncCore(string message, CancellationToken cancel)
    {
        // Make sure we shutdown the connection asynchronously without holding any mutex lock from the caller.
        await Task.Yield();

        await Console.Error.WriteLineAsync($"SHUTTING DOWN {cancel.IsCancellationRequested} {_abortCancelSource.IsCancellationRequested}").ConfigureAwait(false);

        if (_abortCancelSource.IsCancellationRequested)
        {
            throw new ConnectionAbortedException();
        }
        await Console.Error.WriteLineAsync($"SHUTTING DOWN2 {cancel.IsCancellationRequested} {_abortCancelSource.IsCancellationRequested}").ConfigureAwait(false);

        lock (_mutex)
        {
            if (_streams.Count == 0)
            {
                _streamsCompleted.TrySetResult();
            }
        }

        var exception = new ConnectionClosedException(message);

        // TODO: should the on close callback take a nullable exception instead?
        InvokeOnClose(exception);
        await Console.Error.WriteLineAsync($"SHUTTING DOWN3 {cancel.IsCancellationRequested} {_abortCancelSource.IsCancellationRequested}").ConfigureAwait(false);

        // Send GoAway frame.
        IceRpcGoAway goAwayFrame = new(_lastRemoteBidirectionalStreamId, _lastRemoteUnidirectionalStreamId, message);
        await SendControlFrameAsync(
            IceRpcControlFrameType.GoAway,
            (ref SliceEncoder encoder) => goAwayFrame.Encode(ref encoder),
            cancel).ConfigureAwait(false);

        // Wait for the peer to send back a GoAway frame. The task should already be completed if the shutdown has been
        // initiated by the peer.
        await Console.Error.WriteLineAsync("WAIT FOR GOAWAY").ConfigureAwait(false);
        IceRpcGoAway peerGoAwayFrame = await _waitForGoAwayFrame.Task.WaitAsync(cancel).ConfigureAwait(false);

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
        foreach (IMultiplexedStream stream in invocations)
        {
            stream.Abort(exception);
        }

        // Wait for streams to complete.
        await Console.Error.WriteLineAsync("WAIT FOR STREAM TO COMPLETE").ConfigureAwait(false);
        await _streamsCompleted.Task.WaitAsync(cancel).ConfigureAwait(false);
        await Console.Error.WriteLineAsync("WAIT FOR REMOTE STREAM TO COMPLETE").ConfigureAwait(false);

        // Complete the control stream only once all the streams have completed. We also wait for the peer to close
        // its control stream to ensure the peer's stream are also completed. The network connection can safely be
        // closed only once we ensured streams are completed locally and remotely. Otherwise, we could end up
        // closing the network connection too soon, before the remote streams are completed.
        await _controlStream!.Output.CompleteAsync().ConfigureAwait(false);
        _ = await _remoteControlStream!.Input.ReadAsync(cancel).ConfigureAwait(false);

        await Console.Error.WriteLineAsync("WAIT FOR SHUTDOWN").ConfigureAwait(false);

        await _networkConnection.ShutdownAsync(applicationErrorCode: 0, cancel).ConfigureAwait(false);
    }

    private async Task WaitForGoAwayAsync(CancellationToken cancel)
    {
        try
        {
            // Receive and decode GoAway frame
            await ReceiveControlFrameHeaderAsync(IceRpcControlFrameType.GoAway, cancel).ConfigureAwait(false);

            IceRpcGoAway goAwayFrame = await ReceiveGoAwayBodyAsync(cancel).ConfigureAwait(false);

            _waitForGoAwayFrame.SetResult(goAwayFrame);

            lock (_mutex)
            {
                _shutdownTask ??= ShutdownAsyncCore(goAwayFrame.Message, cancel);
            }
        }
        catch (Exception exception)
        {
            _waitForGoAwayFrame.SetException(exception);
        }
    }
}
