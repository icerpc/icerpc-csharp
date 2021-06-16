// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    /// <summary>The stream implementation for Slic. The stream implementation implements flow control to
    /// ensure data isn't buffered indefinitely if the application doesn't consume it. Buffering and flow
    /// control are only enable when EnableReceiveFlowControl is called. Until this is called, the data is
    /// not buffered, instead, the data is not received from the Slic connection until the application protocol
    /// provides a buffer (by calling ReceiveAsync), to receive the data. With Ice2, this means that the
    /// request or response frame is received directly from the Slic connection with intermediate buffering and
    /// data copying and Ice2 enables receive buffering and flow control for receiving the data associated
    /// to a stream a parameter. Enabling buffering only for stream parameters also ensure a lightweight
    /// Slic stream object where no additional heap objects (such as the circular buffer, send semaphore,
    /// etc) are necessary to receive a simple response/request frame.</summary>
    internal class SlicStream : SignaledStream<(int, bool)>
    {
        protected internal override bool ReceivedEndOfStream =>
            (!IsIncoming && !IsBidirectional) || _receivedEndOfStream;
        protected override ReadOnlyMemory<byte> TransportHeader => SlicDefinitions.FrameHeader;

        private volatile CircularBuffer? _receiveBuffer;
        // The receive credit. This is the amount of data received from the peer that we didn't acknowledge as
        // received yet. Once the credit reach a given threeshold, we'll notify the peer with a StreamConsumed
        // frame data has been consumed and additional credit is therefore available for sending.
        private int _receiveCredit;
        private int _receivedOffset;
        private int _receivedSize;
        private bool _receivedEndOfStream;
        // The send credit left for sending data when flow control is enabled. When this reaches 0, no more
        // data can be sent to the peer until the _sendSemaphore is released. The _sendSemaphore will be
        // released when a StreamConsumed frame is received (indicating that the peer has additional space
        // for receiving data).
        private volatile int _sendCredit = int.MaxValue;
        // The semaphore is used when flow control is enabled to wait for additional send credit to be available.
        private AsyncSemaphore? _sendSemaphore;
        private readonly SlicConnection _connection;
        // A lock to ensure ReceivedFrame and EnableReceiveFlowControl are thread-safe.
        private SpinLock _lock;
        // A value which is used as a gate to ensure the stream isn't released twice with the connection.
        private int _streamReleased;

        protected override void AbortWrite(StreamErrorCode errorCode)
        {
            if (IsIncoming && !ReleaseStreamCount())
            {
                // If the stream is already released, it indicates that it was aborted. No need to send a
                // stream Reset frame in this case.
                Debug.Assert(IsAborted);
                return;
            }

            if (!IsAborted)
            {
                // Send the reset frame to the peer.
                _ = _connection.PrepareAndSendFrameAsync(
                    SlicDefinitions.FrameType.StreamReset,
                    ostr =>
                    {
                        checked
                        {
                            new StreamResetBody((ulong)errorCode).IceWrite(ostr);
                        }
                    },
                    frameSize => _connection.Logger.LogSentSlicResetFrame(frameSize, errorCode),
                    this);
            }
        }

        protected override void EnableReceiveFlowControl()
        {
            base.EnableReceiveFlowControl();

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
                signaled = IsSignaled;
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

        protected override void EnableSendFlowControl()
        {
            base.EnableSendFlowControl();

            // Assign the initial send credit based on the peer's stream buffer max size.
            _sendCredit = _connection.PeerStreamBufferMaxSize;

            // Create send semaphore for flow control. The send semaphore ensures that the stream doesn't send
            // more data than it is allowed to the peer.
            _sendSemaphore = new AsyncSemaphore(1);
        }

        protected override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            if (_receivedSize == _receivedOffset)
            {
                if (_receivedEndOfStream)
                {
                    return 0;
                }
                _receivedOffset = 0;
                _receivedSize = 0;

                // Wait to be signaled for the reception of a new stream frame for this stream. If buffering is
                // enabled, check for the circular buffer element count instead of the signal result since
                // multiple Slic frame might have been received and buffered while waiting for the signal.
                (_receivedSize, _receivedEndOfStream) = await WaitAsync(cancel).ConfigureAwait(false);
                if (_receivedSize == 0)
                {
                    if (_receiveBuffer == null)
                    {
                        _connection.FinishedReceivedStreamData(_receivedSize, _receivedEndOfStream, 0);
                    }
                    return 0;
                }
            }

            int size = Math.Min(_receivedSize - _receivedOffset, buffer.Length);
            _receivedOffset += size;
            if (_receiveBuffer == null)
            {
                // Read and append the received stream frame data into the given buffer.
                await _connection.ReceiveDataAsync(buffer.Slice(0, size), CancellationToken.None).ConfigureAwait(false);

                // If we've consumed the whole Slic frame, notify the connection that it can start receiving a new frame.
                if (_receivedOffset == _receivedSize)
                {
                    _connection.FinishedReceivedStreamData(_receivedSize, _receivedEndOfStream, 0);
                }
            }
            else
            {
                // Copy the data from the stream's circular receive buffer to the given buffer.
                Debug.Assert(_receiveBuffer.Count > 0);
                _receiveBuffer.Consume(buffer.Slice(0, size));

                // If we've consumed 75% or more of the circular buffer capacity, notify the peer to allow more data
                // to be sent.
                int consumed = Interlocked.Add(ref _receiveCredit, size);
                if (consumed >= _receiveBuffer.Capacity * 0.75)
                {
                    // Reset _receiveBufferConsumed before notifying the peer.
                    Interlocked.Exchange(ref _receiveCredit, 0);

                    // Notify the peer that it can send additional data.
                    await _connection.PrepareAndSendFrameAsync(
                        SlicDefinitions.FrameType.StreamConsumed,
                        ostr =>
                        {
                            checked
                            {
                                new StreamConsumedBody((ulong)consumed).IceWrite(ostr);
                            }
                        },
                        frameSize => _connection.Logger.LogSentSlicFrame(
                            SlicDefinitions.FrameType.StreamConsumed,
                            frameSize),
                        this,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            return size;
        }

        protected override async ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            // Ensure the caller reserved space for the Slic header by checking for the sentinel header.
            Debug.Assert(TransportHeader.Span.SequenceEqual(buffers.Span[0].Slice(0, TransportHeader.Length).Span));

            int size = buffers.GetByteCount() - TransportHeader.Length;
            if (size == 0)
            {
                // Send an empty last stream frame if there's no data to send. There's no need to check
                // send flow control credit if there's no data to send.
                Debug.Assert(endStream);
                await _connection.SendStreamFrameAsync(this, 0, true, buffers, cancel).ConfigureAwait(false);
                return;
            }

            // The send buffer for the Slic stream frame.
            IList<ReadOnlyMemory<byte>>? sendBuffer = null;

            // The amount of data sent so far.
            int offset = 0;

            // The position of the data to send next.
            var start = new OutputStream.Position();

            while (offset < size)
            {
                if (_sendSemaphore != null)
                {
                    // Acquire the semaphore to ensure flow control allows sending additional data. It's important
                    // to acquire the semaphore before checking _sendMaxSize. The semaphore acquisition will block
                    // if we can't send additional data (_sendMaxSize == 0). Acquiring the semaphore ensures that
                    // we are allowed to send additional data and _sendMaxSize can be used to figure out the size
                    // of the next packet to send.
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
                    lastBuffer = endStream;
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
                        // the first segment of the given protocol buffer.
                        sendBuffer.Clear();
                        sendBuffer.Add(buffers.Span[0].Slice(0, TransportHeader.Length));
                    }

                    // Append data until we reach the allowed packet size or the end of the buffer to send.
                    lastBuffer = false;
                    for (int i = start.Buffer; i < buffers.Length; ++i)
                    {
                        int segmentOffset = i == start.Buffer ? start.Offset : 0;
                        if (buffers.Span[i].Slice(segmentOffset).Length > maxPacketSize - sendSize)
                        {
                            sendBuffer.Add(buffers.Span[i].Slice(segmentOffset, maxPacketSize - sendSize));
                            start = new OutputStream.Position(i, segmentOffset + sendBuffer[^1].Length);
                            Debug.Assert(start.Offset < buffers.Span[i].Length);
                            sendSize = maxPacketSize;
                            break;
                        }
                        else
                        {
                            sendBuffer.Add(buffers.Span[i].Slice(segmentOffset));
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
                        sendSize,
                        lastBuffer && endStream,
                        sendBuffer.ToArray(),
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
                            sendSize,
                            lastBuffer && endStream,
                            sendBuffer.ToArray(),
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

        protected override void Shutdown()
        {
            base.Shutdown();

            // If there's still data pending to be received for the stream, we notify the connection that
            // we're abandoning the reading. It will finish to read the stream's frame data in order to
            // continue receiving frames for other streams.
            if (_receiveBuffer == null)
            {
                try
                {
                    using IDisposable? scope = StartScope();
                    if (_receivedOffset == _receivedSize)
                    {
                        ValueTask<(int, bool)> valueTask = WaitAsync(CancellationToken.None);
                        Debug.Assert(valueTask.IsCompleted);
                        _receivedOffset = 0;
                        (_receivedSize, _receivedEndOfStream) = valueTask.Result;
                        _connection.FinishedReceivedStreamData(_receivedSize, _receivedEndOfStream, _receivedSize);
                    }
                    else
                    {
                        _connection.FinishedReceivedStreamData(
                            _receivedSize,
                            _receivedEndOfStream,
                            _receivedSize - _receivedOffset);
                    }
                }
                catch
                {
                    // Ignore, there's nothing to consume.
                }
            }

            // The stream count is only released on shutdown if it's an incoming stream. Outgoing streams are
            // released from the connection when the StreamLast or StreamReset frame is received (which can be
            // received after the stream is destroyed, for example, with oneway requests, the stream is
            // disposed as soon as the request is sent and before receiving the StreamLast frame).
            if (IsIncoming && ReleaseStreamCount())
            {
                // It's important to decrement the stream count before sending the StreamLast frame to prevent
                // a race where the peer could start a new stream before the counter is decremented.
                _ = _connection.PrepareAndSendFrameAsync(SlicDefinitions.FrameType.StreamLast, stream: this);
            }
        }

        internal SlicStream(SlicConnection connection, long streamId)
            : base(connection, streamId) => _connection = connection;

        internal SlicStream(SlicConnection connection, bool bidirectional, bool control)
            : base(connection, bidirectional, control) => _connection = connection;

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

        internal void ReceivedFrame(int size, bool fin)
        {
            // If an outgoing stream and this is the last stream frame, we release the stream count to
            // eventually allow a new outgoing stream to be opened. If the stream count is already released,
            // there's an issue with the peer sending twice a last stream frame.
            if (!IsIncoming && fin && !ReleaseStreamCount())
            {
                throw new InvalidDataException("stream already released");
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
                        SetResult((size, fin));
                    }
                    catch
                    {
                        // Ignore, the stream has been aborted. Notify the connection that we're not interested
                        // with the data to allow it to receive data for other streams.
                        _connection.FinishedReceivedStreamData(size, fin, size);
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

                    // Receive the data asynchronously. The task will notify the connection when the Slic frame is fully
                    // received to allow the connection to process the next Slic frame.
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
                        // Get a segment from the buffer to receive the data. The buffer might return a smaller
                        // segment than the requested size. If this is the case, we loop to receive the remaining
                        // data in a next available segment.
                        Memory<byte> segment = _receiveBuffer.Enqueue(size - offset);
                        await _connection.ReceiveDataAsync(segment, CancellationToken.None).ConfigureAwait(false);
                        offset += segment.Length;
                    }
                }
                catch (Exception ex)
                {
                    // Socket failure, just set the exception on the stream.
                    SetException(ex);
                }

                // Queue the frame before notifying the connection we're done with the receive. It's important
                // to ensure the received frames are queued in order.
                try
                {
                    QueueResult((size, fin));
                }
                catch
                {
                    // Ignore exceptions, the stream has been aborted.
                }
                _connection.FinishedReceivedStreamData(size, fin, 0);
            }
        }

        internal void ReceivedReset(StreamErrorCode errorCode)
        {
            if (!IsIncoming && !ReleaseStreamCount())
            {
                throw new InvalidDataException("stream already released");
            }

            AbortRead(errorCode);
            CancelDispatchSource?.Cancel();
        }

        internal bool ReleaseStreamCount()
        {
            // Release the stream from the connection if not already done. This will decrease the stream
            // count to allow more streams to be opened.
            if (Interlocked.CompareExchange(ref _streamReleased, 1, 0) == 0)
            {
                if (!IsControl)
                {
                    // If the stream is not already released, releases it now.
                    _connection.ReleaseStream(this);
                }
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
