// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Slic;
using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal.Slic
{
    /// <summary>The stream implementation for Slic. The stream implementation implements flow control to
    /// ensure data isn't buffered indefinitely if the application doesn't consume it. Buffering and flow
    /// control are only enable when EnableReceiveFlowControl is called. Until this is called, the data is not
    /// buffered, instead, the data is not received from the Slic connection until the application protocol
    /// provides a buffer (by calling ReceiveAsync), to receive the data. With Ice2, this means that the
    /// request or response frame is received directly from the Slic connection with intermediate buffering
    /// and data copying and Ice2 enables receive buffering and flow control for receiving the data associated
    /// to a stream a parameter. Enabling buffering only for stream parameters also ensure a lightweight Slic
    /// stream object where no additional heap objects (such as the circular buffer, send semaphore, etc) are
    /// necessary to receive a simple response/request frame.</summary>
    internal class SlicStream : INetworkStream, IValueTaskSource<(int, bool)>
    {
        /// <inheritdoc/>
        public long Id
        {
            get
            {
                if (_id == -1)
                {
                    throw new InvalidOperationException("stream ID isn't allocated yet");
                }
                return _id;
            }
            set
            {
                Debug.Assert(_id == -1);
                _connection.AddStream(value, this, ref _id);
            }
        }

        /// <inheritdoc/>
        public bool IsBidirectional { get; }

        /// <inheritdoc/>
        public Action? ShutdownAction
        {
            get
            {
                bool lockTaken = false;
                try
                {
                    return _shutdownAction;
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }
            }
            set
            {
                bool lockTaken = false;
                try
                {
                    _shutdownAction = value;
                    if (IsShutdown)
                    {
                        _shutdownAction?.Invoke();
                    }
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }
            }
        }

        public ReadOnlyMemory<byte> TransportHeader => SlicDefinitions.FrameHeader;

        internal bool IsRemote => _id != -1 && _id % 2 == (_connection.IsServer ? 0 : 1);
        internal bool IsStarted => _id != -1;
        internal bool WritesCompleted => (Thread.VolatileRead(ref _state) & (int)State.WriteCompleted) > 0;

        private bool IsShutdown => (Thread.VolatileRead(ref _state) & (int)State.Shutdown) > 0;
        private bool ReadsCompleted => (Thread.VolatileRead(ref _state) & (int)State.ReadCompleted) > 0;

        private readonly SlicConnection _connection;
        private long _id = -1;
        private SpinLock _lock;
        private AsyncQueueCore<(int, bool)> _queue = new();
        private readonly ISlicFrameReader _reader;
        private volatile CircularBuffer? _receiveBuffer;
        // The receive credit. This is the amount of data received from the peer that we didn't acknowledge as
        // received yet. Once the credit reach a given threshold, we'll notify the peer with a StreamConsumed
        // frame data has been consumed and additional credit is therefore available for sending.
        private int _receiveCredit;
        private bool _receivedEndStream;
        private int _receivedOffset;
        private int _receivedSize;
        // The send credit left for sending data when flow control is enabled. When this reaches 0, no more
        // data can be sent to the peer until the _sendSemaphore is released. The _sendSemaphore will be
        // released when a StreamConsumed frame is received (indicating that the peer has additional space
        // for receiving data).
        private volatile int _sendCredit = int.MaxValue;
        // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
        private AsyncSemaphore? _sendSemaphore;
        private volatile Action? _shutdownAction;
        private TaskCompletionSource? _shutdownCompletedTaskSource;
        private int _state;
        private CancellationTokenRegistration _tokenRegistration;
        private readonly ISlicFrameWriter _writer;

        public void AbortRead(StreamError errorCode)
        {
            if (ReadsCompleted)
            {
                return;
            }

            // It's important to set the exception before completing the reads because WaitAsync expects the
            // exception to be set if reads are completed.
            _queue.Complete(new StreamAbortedException(errorCode));

            if (TrySetReadCompleted(shutdown: false))
            {
                if (IsStarted && !IsShutdown && errorCode != StreamError.ConnectionAborted)
                {
                    // Notify the peer of the read abort by sending a stop sending frame.
                    _ = SendStopSendingFrameAndShutdownAsync();
                }
                else
                {
                    // Shutdown the stream if not already done.
                    TryShutdown();
                }
            }

            async Task SendStopSendingFrameAndShutdownAsync()
            {
                try
                {
                    await _writer.WriteStreamStopSendingAsync(
                        this,
                        new StreamStopSendingBody((ulong)errorCode),
                        default).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore.
                }
                TryShutdown();
            }
        }

        public void AbortWrite(StreamError errorCode)
        {
            if (IsStarted && !IsShutdown && errorCode != StreamError.ConnectionAborted)
            {
                // Notify the peer of the write abort by sending a reset frame.
                _ = SendResetFrameAndCompleteWritesAsync();
            }
            else
            {
                TrySetWriteCompleted();
            }

            async Task SendResetFrameAndCompleteWritesAsync()
            {
                try
                {
                    await _writer.WriteStreamResetAsync(
                        this,
                        new StreamResetBody((ulong)errorCode),
                        default).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore.
                }
                TrySetWriteCompleted();
            }
        }

        public virtual System.IO.Stream AsByteStream() => new ByteStream(this);

        public void EnableReceiveFlowControl()
        {
            bool signaled;
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);

                // Create a receive buffer to buffer the received stream data. The sender must ensure it doesn't
                // send more data than this receiver allows.
                _receiveBuffer = new CircularBuffer(_connection.StreamBufferMaxSize);

                // If the stream is in the signaled state, the connection is waiting for the frame to be received. In
                // this case we get the frame information and notify again the stream that the frame was received.
                // The frame will be received in the circular buffer and queued.
                signaled = _queue.IsSignaled;
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }

            if (signaled)
            {
                ValueTask<(int, bool)> valueTask = WaitAsync();
                Debug.Assert(valueTask.IsCompleted);
                (int size, bool fin) = valueTask.Result;
                ReceivedFrame(size, fin);
            }
        }

        public void EnableSendFlowControl()
        {
            // Assign the initial send credit based on the peer's stream buffer max size.
            _sendCredit = _connection.PeerStreamBufferMaxSize;

            // Create send semaphore for flow control. The send semaphore ensures that the stream doesn't send
            // more data than it is allowed to the peer.
            _sendSemaphore = new AsyncSemaphore(1);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (_receivedSize == _receivedOffset)
            {
                _receivedOffset = 0;
                _receivedSize = 0;

                // Wait to be signaled for the reception of a new stream frame for this stream. If buffering is
                // enabled, check for the circular buffer element count instead of the signal result since
                // multiple Slic frame might have been received and buffered while waiting for the signal.
                (_receivedSize, _receivedEndStream) = await WaitAsync(cancel).ConfigureAwait(false);

                if (_receivedSize == 0)
                {
                    if (!_receivedEndStream)
                    {
                        throw new InvalidDataException("invalid stream frame, received 0 bytes without end of stream");
                    }
                    TrySetReadCompleted();
                    return 0;
                }
            }

            int size = Math.Min(_receivedSize - _receivedOffset, buffer.Length);
            _receivedOffset += size;
            if (_receiveBuffer == null)
            {
                // Read and append the received stream frame data into the given buffer.
                await _reader.ReadFrameDataAsync(buffer[0..size], CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                // Copy the data from the stream's circular receive buffer to the given buffer.
                Debug.Assert(_receiveBuffer.Count > 0);
                _receiveBuffer.Consume(buffer[0..size]);

                // If we've consumed 75% or more of the circular buffer capacity, notify the peer to allow
                // more data to be sent.
                int consumed = Interlocked.Add(ref _receiveCredit, size);
                if (consumed >= _receiveBuffer.Capacity * 0.75)
                {
                    // Reset _receiveBufferConsumed before notifying the peer.
                    Interlocked.Exchange(ref _receiveCredit, 0);

                    // Notify the peer that it can send additional data.
                    await _writer.WriteStreamConsumedAsync(
                        this,
                        new StreamConsumedBody((ulong)consumed),
                        CancellationToken.None).ConfigureAwait(false);
                }
            }

            if (_receivedOffset == _receivedSize)
            {
                // If we've consumed the whole Slic frame, notify the connection that it can start receiving
                // a new frame if flow control isn't enabled. If flow control is enabled, the data has been
                // buffered and already received.
                if (_receiveBuffer == null)
                {
                    _connection.FinishedReceivedStreamData(0);
                }

                // It's the end of the stream, we can complete reads.
                if (_receivedEndStream)
                {
                    TrySetReadCompleted();
                }
            }

            return size;
        }

        public async ValueTask WriteAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            int size = buffers.GetByteCount() - TransportHeader.Length;
            if (size == 0)
            {
                // Send an empty last stream frame if there's no data to send. There's no need to check send
                // flow control credit if there's no data to send.
                Debug.Assert(endStream);
                await _connection.SendStreamFrameAsync(this, buffers, true, cancel).ConfigureAwait(false);
                return;
            }

            // The send buffer for the Slic stream frame.
            IList<ReadOnlyMemory<byte>>? sendBuffer = null;

            // The amount of data sent so far.
            int offset = 0;

            // The position of the data to send next.
            var start = new BufferWriter.Position();

            while (offset < size)
            {
                if (WritesCompleted)
                {
                    throw new StreamAbortedException(StreamError.StreamAborted);
                }

                if (_sendSemaphore != null)
                {
                    // Acquire the semaphore to ensure flow control allows sending additional data. It's
                    // important to acquire the semaphore before checking _sendCredit. The semaphore
                    // acquisition will block if we can't send additional data (_sendCredit == 0). Acquiring
                    // the semaphore ensures that we are allowed to send additional data and _sendCredit can
                    // be used to figure out the size of the next packet to send.
                    await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                    Debug.Assert(_sendCredit > 0);
                }

                // The maximum packet size to send, it can't be larger than the flow control credit left or
                // the peer's packet max size.
                int maxPacketSize = Math.Min(_sendCredit, _connection.PeerPacketMaxSize);

                int sendSize = 0;
                bool lastBuffer;
                if (sendBuffer == null && size <= maxPacketSize)
                {
                    // The given buffer doesn't need to be fragmented as it's smaller than what we are allowed
                    // to send. We directly send the buffer.
                    sendBuffer = buffers.ToArray();
                    sendSize = size;
                    lastBuffer = true;
                }
                else
                {
                    if (sendBuffer == null)
                    {
                        // Sending first buffer fragment.
                        sendBuffer = new List<ReadOnlyMemory<byte>>(buffers.Length);
                        sendSize = -TransportHeader.Length;
                    }
                    else
                    {
                        // If it's not the first fragment, we re-use the space reserved for the Slic header in
                        // the first buffer of the given protocol buffer.
                        sendBuffer.Clear();
                        sendBuffer.Add(buffers.Span[0][0..TransportHeader.Length]);
                    }

                    // Append data until we reach the allowed packet size or the end of the buffer to send.
                    lastBuffer = false;
                    for (int i = start.Buffer; i < buffers.Length; ++i)
                    {
                        int bufferOffset = i == start.Buffer ? start.Offset : 0;
                        if (buffers.Span[i][bufferOffset..].Length > maxPacketSize - sendSize)
                        {
                            sendBuffer.Add(buffers.Span[i][bufferOffset..(bufferOffset + maxPacketSize - sendSize)]);
                            start = new BufferWriter.Position(i, bufferOffset + sendBuffer[^1].Length);
                            Debug.Assert(start.Offset < buffers.Span[i].Length);
                            sendSize = maxPacketSize;
                            break;
                        }
                        else
                        {
                            sendBuffer.Add(buffers.Span[i][bufferOffset..]);
                            sendSize += sendBuffer[^1].Length;
                            lastBuffer = i + 1 == buffers.Length;
                        }
                    }
                }

                // Send the Slic stream frame.
                offset += sendSize;
                if (_sendSemaphore == null)
                {
                    await _connection.SendStreamFrameAsync(
                        this,
                        sendBuffer.ToArray(),
                        lastBuffer && endStream,
                        cancel).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        // If flow control is enabled, decrease the size of remaining data that we are allowed to
                        // send. If all the credit for sending data is consumed, _sendMaxSize will be 0 and we
                        // don't release the semaphore to prevent further sends. The semaphore will be released
                        // once the stream receives a StreamConsumed frame. It's important to decrease _sendMaxSize
                        // before sending the frame to avoid race conditions where the consumed frame could be
                        // received before we decreased it.
                        int value = Interlocked.Add(ref _sendCredit, -sendSize);

                        await _connection.SendStreamFrameAsync(
                            this,
                            sendBuffer.ToArray(),
                            lastBuffer && endStream,
                            cancel).ConfigureAwait(false);

                        // If flow control allows sending more data, release the semaphore.
                        if (value > 0)
                        {
                            _sendSemaphore.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        _sendSemaphore.Complete(ex);
                        throw;
                    }
                }
            }
        }

        public async ValueTask ShutdownCompleted(CancellationToken cancel)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                _shutdownCompletedTaskSource ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                if (IsShutdown)
                {
                    return;
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
            await _shutdownCompletedTaskSource.Task.WaitAsync(cancel).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override string ToString() => $"{base.ToString()} (ID={Id})";

        internal SlicStream(SlicConnection connection, long streamId, ISlicFrameReader reader, ISlicFrameWriter writer)
        {
            _connection = connection;
            _reader = reader;
            _writer = writer;

            IsBidirectional = streamId % 4 < 2;
            _connection.AddStream(streamId, this, ref _id);

            if (!IsBidirectional)
            {
                // Write-side of remote unidirectional stream is marked as completed.
                TrySetWriteCompleted();
            }
        }

        internal SlicStream(
            SlicConnection connection,
            bool bidirectional,
            ISlicFrameReader reader,
            ISlicFrameWriter writer)
        {
            _connection = connection;
            _reader = reader;
            _writer = writer;

            IsBidirectional = bidirectional;
            if (!IsBidirectional)
            {
                // Read-side of local unidirectional stream is marked as completed.
                TrySetReadCompleted();
            }
        }

        internal void ReceivedConsumed(int size)
        {
            if (_sendSemaphore == null)
            {
                throw new InvalidDataException("invalid stream consumed frame, flow control is not enabled");
            }

            int newValue = Interlocked.Add(ref _sendCredit, size);
            if (newValue == size)
            {
                Debug.Assert(_sendSemaphore.Count == 0);
                _sendSemaphore.Release();
            }
            else if (newValue > 2 * _connection.PeerPacketMaxSize)
            {
                // The peer is trying to increase the credit to a value which is larger than what it is allowed to.
                throw new InvalidDataException("invalid flow control credit increase");
            }
        }

        internal void ReceivedFrame(int size, bool endStream)
        {
            // Receiving a 0-byte StreamLast frame is expected on a local unidirectional stream.
            if (!IsBidirectional && !IsRemote && (size > 0 || !endStream))
            {
                throw new InvalidDataException($"received stream frame on local unidirectional stream");
            }

            // Set the result if buffering is not enabled, the data will be consumed when ReceiveAsync is
            // called. If buffering is enabled, we receive the data and queue the result. The lock needs
            // to be held to ensure thread-safety with EnableReceiveFlowControl which sets the receive
            // buffer.
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (_receiveBuffer == null)
                {
                    try
                    {
                        _queue.SetResult((size, endStream));
                    }
                    catch
                    {
                        // Ignore, the stream has been aborted. Notify the connection that we're not interested
                        // with the data to allow it to receive data for other streams.
                        _connection.FinishedReceivedStreamData(size);
                    }
                }
                else
                {
                    // If the peer sends more data than the circular buffer remaining capacity, it violated flow
                    // control. It's considered as a fatal failure for the connection.
                    if (size > _receiveBuffer.Available)
                    {
                        throw new InvalidDataException("flow control violation, peer sent too much data");
                    }

                    // Receive the data asynchronously. The task will notify the connection when the Slic
                    // frame is fully received to allow the connection to process the next Slic frame.
                    _ = PerformReceiveInBufferAsync();
                }
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }

            async Task PerformReceiveInBufferAsync()
            {
                Debug.Assert(_receiveBuffer != null);
                try
                {
                    // Read and append the received data into the circular buffer.
                    for (int offset = 0; offset < size;)
                    {
                        // Get a chunk from the buffer to receive the data. The buffer might return a smaller
                        // chunk than the requested size. If this is the case, we loop to receive the remaining
                        // data in a next available chunk.
                        Memory<byte> chunk = _receiveBuffer.Enqueue(size - offset);
                        await _reader.ReadFrameDataAsync(chunk, CancellationToken.None).ConfigureAwait(false);
                        offset += chunk.Length;
                    }
                }
                catch
                {
                    // Socket failure, just set the exception on the stream.
                    AbortRead(StreamError.ConnectionAborted);
                }

                // Queue the frame before notifying the connection we're done with the receive. It's important
                // to ensure the received frames are queued in order.
                try
                {
                    _queue.Queue((size, endStream));
                }
                catch
                {
                    // Ignore exceptions, the stream has been aborted.
                }

                if (size > 0)
                {
                    _connection.FinishedReceivedStreamData(0);
                }
            }
        }

        internal void ReceivedReset(StreamError errorCode)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException("received reset frame on local unidirectional stream");
            }

            // It's important to set the exception before completing the reads because ReceiveAsync expects the
            // exception to be set if reads are completed.
            _queue.Complete(new StreamAbortedException(errorCode));

            TrySetReadCompleted();
        }

        internal bool TrySetReadCompleted(bool shutdown = true) => TrySetState(State.ReadCompleted, shutdown);

        internal bool TrySetWriteCompleted() => TrySetState(State.WriteCompleted, true);

        (int, bool) IValueTaskSource<(int, bool)>.GetResult(short token)
        {
            // Reset the source to allow the stream to be signaled again. It's important to dispose the
            // registration without the lock held since Dispose() might block until the cancellation callback
            // is completed if the cancellation callback is running (and potentially trying to acquire the
            // lock to set the exception).
            _tokenRegistration.Dispose();
            _tokenRegistration = default;

            return _queue.GetResult(token);
        }

        ValueTaskSourceStatus IValueTaskSource<(int, bool)>.GetStatus(short token) => _queue.GetStatus(token);

        void IValueTaskSource<(int, bool)>.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) =>
            _queue.OnCompleted(continuation, state, token, flags);

        private void Shutdown()
        {
            Debug.Assert(_state == (int)(State.ReadCompleted | State.WriteCompleted | State.Shutdown));
            bool lockTaken = false;
            try
            {
                try
                {
                    ShutdownAction?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.Assert(false, $"unexpected exception {ex}");
                    throw;
                }
                _shutdownCompletedTaskSource?.SetResult();
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }
            _connection.RemoveStream(Id);

            // The stream might not be signaled if it's shutdown gracefully after receiving endStream. We make
            // sure to set the exception in this case to prevent WaitAsync calls to block.
            _queue.Complete(new StreamAbortedException(StreamError.StreamAborted));

            _tokenRegistration.Dispose();

            // If there's still data pending to be received for the stream, we notify the connection that
            // we're abandoning the reading. It will finish to read the stream's frame data in order to
            // continue receiving frames for other streams.
            if (_receiveBuffer == null)
            {
                if (_receivedOffset == _receivedSize)
                {
                    ValueTask<(int, bool)> valueTask = WaitAsync(CancellationToken.None);
                    Debug.Assert(valueTask.IsCompleted);
                    if (valueTask.IsCompletedSuccessfully)
                    {
                        _receivedOffset = 0;
                        (_receivedSize, _) = valueTask.Result;
                    }
                }

                if (_receivedSize - _receivedOffset > 0)
                {
                    _connection.FinishedReceivedStreamData(_receivedSize - _receivedOffset);
                }
            }

            // Release connection stream count or semaphore for this stream.
            _connection.ReleaseStream(this);

            // Local streams are released from the connection when the StreamLast or StreamReset frame is
            // received. Since a remote un-directional stream doesn't send stream frames, we have to send a
            // stream last frame here to ensure the local stream is released from the connection.
            if (IsRemote && !IsBidirectional)
            {
                // It's important to decrement the stream count before sending the StreamLast frame to prevent
                // a race where the peer could start a new stream before the counter is decremented.
                _writer.WriteStreamFrameAsync(
                    this,
                    new ReadOnlyMemory<byte>[] { SlicDefinitions.FrameHeader.ToArray() },
                    true,
                    default).AsTask();
            }
        }

        private void TryShutdown()
        {
            // If both reads and writes are completed, the stream is started and not already shutdown, call
            // shutdown.
            if (ReadsCompleted && WritesCompleted && TrySetState(State.Shutdown, false) && IsStarted)
            {
                try
                {
                    Shutdown();
                }
                catch (Exception exception)
                {
                    Debug.Assert(false, $"unexpected exception {exception}");
                }
            }
        }

        private bool TrySetState(State state, bool shutdown)
        {
            if (((State)Interlocked.Or(ref _state, (int)state)).HasFlag(state))
            {
                return false;
            }
            else
            {
                if (shutdown)
                {
                    TryShutdown();
                }
                return true;
            }
        }

        private ValueTask<(int, bool)> WaitAsync(CancellationToken cancel = default)
        {
            // TODO: XXX still needed?
            // if (ReadsCompleted && _exception == null)
            // {
            //     // If reads are completed and no exception is set, it's probably because ReceiveAsync is called after
            //     // receiving the end stream flag.
            //     throw new InvalidOperationException("reads are completed");
            // }

            if (cancel.CanBeCanceled)
            {
                Debug.Assert(_tokenRegistration == default);
                cancel.ThrowIfCancellationRequested();
                _tokenRegistration = cancel.Register(() => _queue.SetException(new OperationCanceledException()));
            }
            return new ValueTask<(int, bool)>(this, _queue.Version);
        }

        // A System.IO.Stream class to wrap SendAsync/ReceiveAsync functionality of the RpcStream. For Quic,
        // this won't be needed since the QuicStream is a System.IO.Stream.
        private class ByteStream : System.IO.Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotImplementedException();

            public override long Position
            {
                get => throw new NotImplementedException();
                set => throw new NotImplementedException();
            }

            private readonly ReadOnlyMemory<byte>[] _buffers;
            private readonly SlicStream _stream;

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                ReadAsync(new Memory<byte>(buffer, offset, count), cancel).AsTask();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
            {
                try
                {
                    if (_stream.ReadsCompleted)
                    {
                        return 0;
                    }
                    return await _stream.ReadAsync(buffer, cancel).ConfigureAwait(false);
                }
                catch (StreamAbortedException ex) when (ex.ErrorCode == StreamError.StreamingCanceledByWriter)
                {
                    throw new System.IO.IOException("streaming canceled by the writer", ex);
                }
                catch (StreamAbortedException ex) when (ex.ErrorCode == StreamError.StreamingCanceledByReader)
                {
                    throw new System.IO.IOException("streaming canceled by the reader", ex);
                }
                catch (StreamAbortedException ex)
                {
                    throw new System.IO.IOException($"unexpected streaming error {ex.ErrorCode}", ex);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException($"unexpected exception", ex);
                }
            }

            public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotImplementedException();

            public override void SetLength(long value) => throw new NotImplementedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                WriteAsync(new Memory<byte>(buffer, offset, count), cancel).AsTask();

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
            {
                try
                {
                    _buffers[^1] = buffer;
                    await _stream.WriteAsync(_buffers, buffer.Length == 0, cancel).ConfigureAwait(false);
                }
                catch (StreamAbortedException ex) when (ex.ErrorCode == StreamError.StreamingCanceledByWriter)
                {
                    throw new System.IO.IOException("streaming canceled by the writer", ex);
                }
                catch (StreamAbortedException ex) when (ex.ErrorCode == StreamError.StreamingCanceledByReader)
                {
                    throw new System.IO.IOException("streaming canceled by the reader", ex);
                }
                catch (StreamAbortedException ex)
                {
                    throw new System.IO.IOException($"unexpected streaming error {ex.ErrorCode}", ex);
                }
                catch (Exception ex)
                {
                    throw new System.IO.IOException($"unexpected exception", ex);
                }
            }

            internal ByteStream(SlicStream stream)
            {
                _stream = stream;
                if (_stream.TransportHeader.Length > 0)
                {
                    _buffers = new ReadOnlyMemory<byte>[2];
                    _buffers[0] = _stream.TransportHeader.ToArray();
                }
                else
                {
                    _buffers = new ReadOnlyMemory<byte>[1];
                }
            }
        }

        private enum State : int
        {
            ReadCompleted = 1,
            WriteCompleted = 2,
            Shutdown = 4
        }

    }
}
