// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal
{
    /// <summary>The stream implementation for Slic. The stream implementation implements flow control to ensure data
    /// isn't buffered indefinitely if the application doesn't consume it. Buffering and flow control are only enable
    /// when sending multiple Slic packet or if the Slic packet size exceeds the peer packet maximum size.</summary>
    internal class SlicMultiplexedStream : IMultiplexedStream, IAsyncQueueValueTaskSource<(int, bool)>
    {
        /// <inheritdoc/>
        public long Id
        {
            get
            {
                long id = Thread.VolatileRead(ref _id);
                if (id == -1)
                {
                    throw new InvalidOperationException("stream ID isn't allocated yet");
                }
                return id;
            }
            set
            {
                Debug.Assert(_id == -1);
                Thread.VolatileWrite(ref _id, value);
            }
        }

        /// <inheritdoc/>
        public bool IsBidirectional { get; }

        /// <inheritdoc/>
        public bool IsStarted => Thread.VolatileRead(ref _id) != -1;

        /// <inheritdoc/>
        public Action? ShutdownAction
        {
            get
            {
                bool lockTaken = false;
                try
                {
                    _lock.Enter(ref lockTaken);
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
                bool alreadyShutdown = false;
                try
                {
                    _lock.Enter(ref lockTaken);
                    if (IsShutdown)
                    {
                        alreadyShutdown = true;
                    }
                    else
                    {
                        _shutdownAction = value;
                    }
                }
                finally
                {
                    if (lockTaken)
                    {
                        _lock.Exit();
                    }
                }

                if (alreadyShutdown)
                {
                    value?.Invoke();
                }
            }
        }

        /// <summary>Specifies whether or not this is a stream initiated by the peer.</summary>
        internal bool IsRemote => _id != -1 && _id % 2 == (_connection.IsServer ? 0 : 1);

        /// <summary>The stream reset error code is set if the stream is reset by the peer or locally. It's used to set
        /// the correct error code for <see cref="MultiplexedStreamAbortedException"/> raised by the stream.</summary>
        internal byte? ResetErrorCode { get; private set; }

        internal bool WritesCompleted => ((State)Thread.VolatileRead(ref _state)).HasFlag(State.WriteCompleted);

        private bool IsShutdown =>
            ((State)Thread.VolatileRead(ref _state)).HasFlag(State.WriteCompleted | State.ReadCompleted);

        private bool ReadsCompleted => ((State)Thread.VolatileRead(ref _state)).HasFlag(State.ReadCompleted);

        private readonly SlicNetworkConnection _connection;
        private long _id = -1;
        private SpinLock _lock;

        // TODO: remove pragma warning disable/restore once analyser is fixed.
        // It is necessary to call new() explicitly to execute the parameterless ctor of AsyncQueueCore, which is
        // synthesized from AsyncQueueCore fields defaults.
#pragma warning disable CA1805 // member is explicitly initialized to its default value
        private AsyncQueueCore<(int, bool)> _queue = new();
#pragma warning restore CA1805

        private readonly ISlicFrameReader _reader;
        private CircularBuffer _receiveBuffer;
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
        private readonly ISlicFrameWriter _writer;

        public void AbortRead(byte errorCode)
        {
            if (ReadsCompleted)
            {
                return;
            }

            // Unblock ReadAsync which is blocked on _queue.WaitAsync()
            ResetErrorCode = errorCode;
            try
            {
                _queue.Enqueue((0, true));
            }
            catch (OperationCanceledException)
            {
                // Ignore.
            }

            if (IsStarted && !IsShutdown)
            {
                // Notify the peer of the read abort by sending a stop sending frame.
                _ = SendStopSendingFrameAndShutdownAsync();
            }
            else
            {
                TrySetReadCompleted();
            }

            async Task SendStopSendingFrameAndShutdownAsync()
            {
                try
                {
                    await _writer.WriteStreamStopSendingAsync(
                        this,
                        new StreamStopSendingBody(errorCode),
                        default).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore.
                }
                TrySetReadCompleted();
            }
        }

        public void AbortWrite(byte errorCode)
        {
            ResetErrorCode = errorCode;

            if (IsStarted && !IsShutdown)
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
                        new StreamResetBody(errorCode),
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

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (ReadsCompleted)
            {
                if (ResetErrorCode != null)
                {
                    throw new MultiplexedStreamAbortedException(ResetErrorCode.Value);
                }
                else
                {
                    return 0;
                }
            }

            if (_receivedSize == _receivedOffset)
            {
                _receivedOffset = 0;
                _receivedSize = 0;

                // Wait to be signaled for the reception of a new stream frame for this stream.
                (_receivedSize, _receivedEndStream) = await _queue.DequeueAsync(this, cancel).ConfigureAwait(false);

                if (_receivedSize == 0)
                {
                    if (!_receivedEndStream)
                    {
                        throw new InvalidDataException("empty Slic frame requires endStream to be set");
                    }
                    if (ResetErrorCode is byte streamError)
                    {
                        throw new MultiplexedStreamAbortedException(streamError);
                    }
                    TrySetReadCompleted();
                    return 0;
                }
            }

            int size = Math.Min(_receivedSize - _receivedOffset, buffer.Length);
            _receivedOffset += size;

            // Copy the data from the stream's circular receive buffer to the given buffer.
            _receiveBuffer.Consume(buffer[0..size]);

            // If we've consumed 75% or more of the circular buffer capacity, notify the peer to allow
            // more data to be sent.
            // TODO: is 75% a good setting?
            int consumed = Interlocked.Add(ref _receiveCredit, size);
            if (consumed >= _connection.StreamBufferMaxSize * 0.75)
            {
                // Reset _receiveCredit before notifying the peer.
                Interlocked.Exchange(ref _receiveCredit, 0);

                // Notify the peer that it can send additional data.
                await _writer.WriteStreamConsumedAsync(
                    this,
                    new StreamConsumedBody((ulong)consumed),
                    CancellationToken.None).ConfigureAwait(false);
            }

            // It's the end of the stream, we can complete reads.
            if (_receivedOffset == _receivedSize && _receivedEndStream)
            {
                TrySetReadCompleted();
            }

            return size;
        }

        public async ValueTask WriteAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            int size = buffers.GetByteCount();
            if (size == 0)
            {
                // Send an empty last stream frame if there's no data to send. There's no need to check send flow
                // control credit if there's no data to send.
                Debug.Assert(endStream);
                await _connection.SendStreamFrameAsync(
                    this,
                    new ReadOnlyMemory<byte>[] { SlicDefinitions.FrameHeader.ToArray() },
                    true,
                    cancel).ConfigureAwait(false);
                return;
            }

            // If we are about to send data which is larger than what the peer allows or if there's more data to
            // come, we enable flow control.
            if (_sendSemaphore == null && (!endStream || size > _connection.PeerStreamBufferMaxSize))
            {
                // Assign the initial send credit based on the peer's stream buffer max size.
                _sendCredit = _connection.PeerStreamBufferMaxSize;

                // Create send semaphore for flow control. The send semaphore ensures that the stream doesn't send more
                // data to the peer than its send credit.
                _sendSemaphore = new AsyncSemaphore(1);
            }

            // The send buffers for the Slic stream frame. Reserve an additional buffer for the Slic header.
            var sendBuffers = new ReadOnlyMemory<byte>[buffers.Length + 1];
            Memory<byte> headerBuffer = SlicDefinitions.FrameHeader.ToArray();

            // The amount of data sent so far.
            int offset = 0;

            // The position of the data to send next.
            var start = new BufferWriter.Position();

            while (offset < size)
            {
                if (WritesCompleted)
                {
                    if (ResetErrorCode is byte resetErrorCode)
                    {
                        // If the stream has been aborted by a reset, raise StreamAbortedException
                        throw new MultiplexedStreamAbortedException(resetErrorCode);
                    }
                    else
                    {
                        // Otherwise if writes completed normally, the caller is bogus, it shouldn't call WriteAsync
                        // after performing a write with endStream=true.
                        throw new InvalidOperationException("the stream writes are already completed");
                    }
                }

                if (_sendSemaphore != null)
                {
                    // Acquire the semaphore to ensure flow control allows sending additional data. It's important to
                    // acquire the semaphore before checking _sendCredit. The semaphore acquisition will block if we
                    // can't send additional data (_sendCredit == 0). Acquiring the semaphore ensures that we are
                    // allowed to send additional data and _sendCredit can be used to figure out the size of the next
                    // packet to send.
                    await _sendSemaphore.EnterAsync(cancel).ConfigureAwait(false);
                    Debug.Assert(_sendCredit > 0);
                }

                // The maximum packet size to send, it can't be larger than the flow control credit left or
                // the peer's packet max size.
                int maxPacketSize = Math.Min(_sendCredit, _connection.PeerPacketMaxSize);
                int sendSize = 0;

                int sendBufferIdx = 0;
                sendBuffers[sendBufferIdx++] = headerBuffer;

                // Append data until we reach the allowed packet size or the end of the buffer to send.
                for (int i = start.Buffer; i < buffers.Length; ++i)
                {
                    int bufferOffset = i == start.Buffer ? start.Offset : 0;

                    // Send the full buffer if there's still space left on the Slic packet. Otherwise, only send a chunk
                    // of the buffer large enough to fill the Slic packet.
                    int bufferSize = Math.Min(buffers.Span[i][bufferOffset..].Length, maxPacketSize - sendSize);
                    sendBuffers[sendBufferIdx++] = buffers.Span[i][bufferOffset..(bufferOffset + bufferSize)];

                    sendSize += bufferSize;

                    if (sendSize == maxPacketSize)
                    {
                        // No space left on the Slic packet, remember the send position for the next packet.
                        start = new BufferWriter.Position(i, bufferOffset + bufferSize);
                        break;
                    }
                }

                // Send the Slic stream frame.
                offset += sendSize;
                if (_sendSemaphore == null)
                {
                    await _connection.SendStreamFrameAsync(
                        this,
                        sendBuffers.AsMemory()[..sendBufferIdx],
                        endStream: (offset == size) && endStream,
                        cancel).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        // If flow control is enabled, decrease the size of remaining data that we are allowed to send.
                        // If all the credit for sending data is consumed, _sendMaxSize will be 0 and we don't release
                        // the semaphore to prevent further sends. The semaphore will be released once the stream
                        // receives a StreamConsumed frame. It's important to decrease _sendCredit before sending the
                        // frame to avoid race conditions where the consumed frame could be received before we decreased
                        // it.
                        int value = Interlocked.Add(ref _sendCredit, -sendSize);

                        await _connection.SendStreamFrameAsync(
                            this,
                            sendBuffers.AsMemory()[..sendBufferIdx],
                            endStream: (offset == size) && endStream,
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

        public async Task WaitForShutdownAsync(CancellationToken cancel)
        {
            bool lockTaken = false;
            try
            {
                _lock.Enter(ref lockTaken);
                if (IsShutdown)
                {
                    return;
                }
                _shutdownCompletedTaskSource ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
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

        internal SlicMultiplexedStream(
            SlicNetworkConnection connection,
            bool bidirectional,
            bool remote,
            ISlicFrameReader reader,
            ISlicFrameWriter writer)
        {
            _connection = connection;
            _reader = reader;
            _writer = writer;
            _receiveBuffer = new CircularBuffer(_connection.StreamBufferMaxSize);

            IsBidirectional = bidirectional;
            if (!IsBidirectional)
            {
                if (remote)
                {
                    // Write-side of remote unidirectional stream is marked as completed.
                    TrySetWriteCompleted();
                }
                else
                {
                    // Read-side of local unidirectional stream is marked as completed.
                    TrySetReadCompleted();
                }
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

        internal async ValueTask ReceivedFrameAsync(int size, bool endStream)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException($"received stream frame on local unidirectional stream");
            }
            else if (size == 0 && !endStream)
            {
                throw new InvalidDataException("invalid stream frame, received 0 bytes without end of stream");
            }

            // Read and append the received data into the circular buffer.
            for (int offset = 0; offset < size;)
            {
                // Get a chunk from the buffer to receive the data. The buffer might return a smaller chunk than the
                // requested size. If this is the case, we loop to receive the remaining data in a next available chunk.
                // Enqueue will raise if the sender sent too much data. This will result in the connection closure.
                Memory<byte> chunk = _receiveBuffer.Enqueue(size - offset);
                await _reader.ReadFrameDataAsync(chunk, CancellationToken.None).ConfigureAwait(false);
                offset += chunk.Length;
            }

            try
            {
                _queue.Enqueue((size, endStream));
            }
            catch (OperationCanceledException)
            {
                // Ignore, ReadAsync has been canceled.
            }
        }

        internal void ReceivedReset(byte errorCode)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException("received reset frame on local unidirectional stream");
            }

            // Unblock ReadAsync which is blocked on _queue.WaitAsync()
            ResetErrorCode = errorCode;
            try
            {
                _queue.Enqueue((0, true));
            }
            catch (OperationCanceledException)
            {
                // Ignore, ReadAsync has been canceled.
            }

            TrySetReadCompleted();
        }

        internal void ReceivedStopSending(byte errorCode)
        {
            if (!IsBidirectional && !IsRemote)
            {
                throw new InvalidDataException("received reset frame on local unidirectional stream");
            }

            ResetErrorCode = errorCode;
            TrySetWriteCompleted();
        }

        internal bool TrySetReadCompleted() => TrySetState(State.ReadCompleted);

        internal bool TrySetWriteCompleted() => TrySetState(State.WriteCompleted);

        (int, bool) IValueTaskSource<(int, bool)>.GetResult(short token) => _queue.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource<(int, bool)>.GetStatus(short token) => _queue.GetStatus(token);

        void IValueTaskSource<(int, bool)>.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) => _queue.OnCompleted(continuation, state, token, flags);

        void IAsyncQueueValueTaskSource<(int, bool)>.Cancel() => _queue.TryComplete(new OperationCanceledException());

        private void Shutdown()
        {
            Debug.Assert(_state == (int)(State.ReadCompleted | State.WriteCompleted));
            bool lockTaken = false;
            Action? shutdownAction = null;
            try
            {
                _lock.Enter(ref lockTaken);
                shutdownAction = _shutdownAction;
                _shutdownCompletedTaskSource?.SetResult();
            }
            finally
            {
                if (lockTaken)
                {
                    _lock.Exit();
                }
            }

            try
            {
                shutdownAction?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.Assert(false, $"unexpected exception from stream shutdown action {ex}");
                throw;
            }

            if (IsStarted)
            {
                // Release connection stream count or semaphore for this stream and remove it from the factory.
                _connection.ReleaseStream(this);

                // Local streams are released from the connection when the StreamLast or StreamReset frame is received.
                // Since a remote unidirectional stream doesn't send stream frames, we have to send a
                // UnidirectionalStreamReleased frame here to ensure the local stream is released from the connection by
                // the peer. It's important to decrement the stream count with the ReleaseStream call above to prevent a
                // race where the peer could start a new stream before the counter is decreased
                if (IsRemote && !IsBidirectional)
                {
                    try
                    {
                        _ = _writer.WriteUnidirectionalStreamReleasedAsync(this, default).AsTask();
                    }
                    catch
                    {
                        // Ignore, the connection is closed.
                    }
                }
            }

            // We're done with the receive buffer.
            _receiveBuffer.Dispose();
        }

        private bool TrySetState(State state)
        {
            var previousState = (State)Interlocked.Or(ref _state, (int)state);
            State newState = previousState | state;
            if (previousState == newState)
            {
                // The given state was already set.
                return false;
            }
            else if (newState == (State.ReadCompleted | State.WriteCompleted))
            {
                // The stream reads and writes are completed, shutdown the stream.
                try
                {
                    Shutdown();
                }
                catch (Exception exception)
                {
                    Debug.Assert(false, $"unexpected exception {exception}");
                }
            }
            return true;
        }

        // A System.IO.Stream class to wrap WriteAsync/ReadAsync functionality of the SlicMultiplexedStream. For Quic,
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

            private readonly ReadOnlyMemory<byte>[] _buffers = new ReadOnlyMemory<byte>[1];
            private readonly SlicMultiplexedStream _stream;

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                ReadAsync(new Memory<byte>(buffer, offset, count), cancel).AsTask();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
            {
                if (_stream.ReadsCompleted)
                {
                    return 0;
                }
                return await _stream.ReadAsync(buffer, cancel).ConfigureAwait(false);
            }

            public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotImplementedException();

            public override void SetLength(long value) => throw new NotImplementedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                WriteAsync(new Memory<byte>(buffer, offset, count), cancel).AsTask();

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
            {
                _buffers[0] = buffer;
                await _stream.WriteAsync(_buffers, buffer.Length == 0, cancel).ConfigureAwait(false);
            }

            internal ByteStream(SlicMultiplexedStream stream) => _stream = stream;
        }

        [Flags]
        private enum State : byte
        {
            ReadCompleted = 1,
            WriteCompleted = 2,
        }
    }
}
