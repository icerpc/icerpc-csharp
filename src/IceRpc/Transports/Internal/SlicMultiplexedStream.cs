// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal
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
    internal class SlicMultiplexedStream : IMultiplexedStream, IAsyncQueueValueTaskSource<(int, bool)>
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
                _streamFactory.AddStream(value, this, ref _id);
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

        public ReadOnlyMemory<byte> TransportHeader => SlicDefinitions.FrameHeader;

        internal bool IsRemote => _id != -1 && _id % 2 == (_streamFactory.IsServer ? 0 : 1);
        internal bool IsStarted => _id != -1;
        internal bool WritesCompleted => (Thread.VolatileRead(ref _state) & (int)State.WriteCompleted) > 0;

        private bool IsShutdown => (Thread.VolatileRead(ref _state) & (int)State.Shutdown) > 0;
        private bool ReadsCompleted => (Thread.VolatileRead(ref _state) & (int)State.ReadCompleted) > 0;

        private readonly SlicMultiplexedStreamFactory _streamFactory;
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

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (_receivedSize == _receivedOffset)
            {
                _receivedOffset = 0;
                _receivedSize = 0;

                // Wait to be signaled for the reception of a new stream frame for this stream. If buffering is
                // enabled, check for the circular buffer element count instead of the signal result since
                // multiple Slic frame might have been received and buffered while waiting for the signal.
                (_receivedSize, _receivedEndStream) = await _queue.WaitAsync(this, cancel).ConfigureAwait(false);

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

            // Copy the data from the stream's circular receive buffer to the given buffer.
            _receiveBuffer.Consume(buffer[0..size]);

            // If we've consumed 75% or more of the circular buffer capacity, notify the peer to allow
            // more data to be sent.
            // TODO: is 75% a good setting?
            int consumed = Interlocked.Add(ref _receiveCredit, size);
            if (consumed >= _streamFactory.StreamBufferMaxSize * 0.75)
            {
                // Reset _receiveBufferConsumed before notifying the peer.
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
            int size = buffers.GetByteCount() - TransportHeader.Length;
            if (size == 0)
            {
                // Send an empty last stream frame if there's no data to send. There's no need to check send flow
                // control credit if there's no data to send.
                Debug.Assert(endStream);
                await _streamFactory.SendStreamFrameAsync(this, buffers, true, cancel).ConfigureAwait(false);
                return;
            }

            // If we are about to send data which is larger than what the peer allows or if there's more data to
            // come, we enable flow control.
            if (_sendSemaphore == null && (!endStream || size > _streamFactory.PeerStreamBufferMaxSize))
            {
                // Assign the initial send credit based on the peer's stream buffer max size.
                _sendCredit = _streamFactory.PeerStreamBufferMaxSize;

                // Create send semaphore for flow control. The send semaphore ensures that the stream doesn't send more
                // data to the peer than its send credit.
                _sendSemaphore = new AsyncSemaphore(1);
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
                int maxPacketSize = Math.Min(_sendCredit, _streamFactory.PeerPacketMaxSize);

                int sendSize = 0;
                bool lastBuffer;
                if (sendBuffer == null && size <= maxPacketSize)
                {
                    // The given buffer doesn't need to be fragmented as it's smaller than what we are allowed to send.
                    // We directly send the buffer.
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
                        // If it's not the first fragment, we re-use the space reserved for the Slic header in the first
                        // buffer of the given protocol buffer.
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
                    await _streamFactory.SendStreamFrameAsync(
                        this,
                        sendBuffer.ToArray(),
                        lastBuffer && endStream,
                        cancel).ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        // If flow control is enabled, decrease the size of remaining data that we are allowed to send.
                        // If all the credit for sending data is consumed, _sendMaxSize will be 0 and we don't release
                        // the semaphore to prevent further sends. The semaphore will be released once the stream
                        // receives a StreamConsumed frame. It's important to decrease _sendMaxSize before sending the
                        // frame to avoid race conditions where the consumed frame could be received before we decreased
                        // it.
                        int value = Interlocked.Add(ref _sendCredit, -sendSize);

                        await _streamFactory.SendStreamFrameAsync(
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
            SlicMultiplexedStreamFactory streamFactory,
            long streamId,
            ISlicFrameReader reader,
            ISlicFrameWriter writer)
        {
            _streamFactory = streamFactory;
            _reader = reader;
            _writer = writer;

            IsBidirectional = streamId % 4 < 2;
            _streamFactory.AddStream(streamId, this, ref _id);
            _receiveBuffer = new CircularBuffer(_streamFactory.StreamBufferMaxSize);

            if (!IsBidirectional)
            {
                // Write-side of remote unidirectional stream is marked as completed.
                TrySetWriteCompleted();
            }
        }

        internal SlicMultiplexedStream(
            SlicMultiplexedStreamFactory connection,
            bool bidirectional,
            ISlicFrameReader reader,
            ISlicFrameWriter writer)
        {
            _streamFactory = connection;
            _reader = reader;
            _writer = writer;
            _receiveBuffer = new CircularBuffer(_streamFactory.StreamBufferMaxSize);

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
            else if (newValue > 2 * _streamFactory.PeerPacketMaxSize)
            {
                // The peer is trying to increase the credit to a value which is larger than what it is allowed to.
                throw new InvalidDataException("invalid flow control credit increase");
            }
        }

        internal async ValueTask ReceivedFrameAsync(int size, bool endStream)
        {
            // Receiving a 0-byte StreamLast frame is expected on a local unidirectional stream.
            if (!IsBidirectional && !IsRemote && (size > 0 || !endStream))
            {
                throw new InvalidDataException($"received stream frame on local unidirectional stream");
            }

            // Read and append the received data into the circular buffer.
            for (int offset = 0; offset < size;)
            {
                // Get a chunk from the buffer to receive the data. The buffer might return a smaller chunk than the
                // requested size. If this is the case, we loop to receive the remaining data in a next available chunk.
                // Enqueue will raise if the sender sent too much data. This will result in the connection closure.
                Memory<byte> chunk = _receiveBuffer.Enqueue(size - offset);
                try
                {
                    await _reader.ReadFrameDataAsync(chunk, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Socket failure, just set the exception on the stream.
                    AbortRead(StreamError.ConnectionAborted);
                    break;
                }
                offset += chunk.Length;
            }

            try
            {
                _queue.Queue((size, endStream));
            }
            catch
            {
                // Ignore exceptions, the stream has been aborted.
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

        (int, bool) IValueTaskSource<(int, bool)>.GetResult(short token) => _queue.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource<(int, bool)>.GetStatus(short token) => _queue.GetStatus(token);

        void IAsyncQueueValueTaskSource<(int, bool)>.SetException(Exception ex) => _queue.SetException(ex);

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
                _lock.Enter(ref lockTaken);
                try
                {
                    _shutdownAction?.Invoke();
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

            if (IsStarted)
            {
                // Release connection stream count or semaphore for this stream and remove it from the factory.
                _streamFactory.ReleaseStream(this);

                // The stream might not be signaled if it's shutdown gracefully after receiving endStream. We make sure
                // to set the exception in this case to prevent ReadAsync from blocking.
                _queue.Complete(new StreamAbortedException(StreamError.StreamAborted));

                // Local streams are released from the connection when the StreamLast or StreamReset frame is received.
                // Since a remote un-directional stream doesn't send stream frames, we have to send a stream last frame
                // here to ensure the local stream is released from the connection.
                if (IsRemote && !IsBidirectional)
                {
                    // It's important to decrement the stream count before sending the StreamLast frame to prevent a race
                    // where the peer could start a new stream before the counter is decremented.
                    _writer.WriteStreamFrameAsync(
                        this,
                        new ReadOnlyMemory<byte>[] { SlicDefinitions.FrameHeader.ToArray() },
                        true,
                        default).AsTask();
                }
            }

            // We're done with the receive buffer.
            _receiveBuffer.Dispose();
        }

        private void TryShutdown()
        {
            // If both reads and writes are completed, the stream is started and not already shutdown, call
            // shutdown.
            if (ReadsCompleted && WritesCompleted && TrySetState(State.Shutdown, false))
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
            private readonly SlicMultiplexedStream _stream;

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

            internal ByteStream(SlicMultiplexedStream stream)
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
