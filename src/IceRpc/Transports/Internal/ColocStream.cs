// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Diagnostics;

namespace IceRpc.Transports.Internal
{
    /// <summary>The RpcStream class for the colocated transport.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Microsoft.Design",
        "CA1001:Type 'ColocStream' owns disposable field(s) '_sendSemaphore' but is not disposable",
        Justification = "_sendSemaphore is disposed by Shutdown")]
    internal class ColocStream : SignaledStream<(object, bool)>
    {
        private ReadOnlyMemory<ReadOnlyMemory<byte>> _receivedBuffers;
        private bool _receivedEndStream;
        private (int BufferIndex, int Offset) _receivedPos;
        private readonly ColocConnection _connection;
        private SemaphoreSlim? _sendSemaphore;
        private SemaphoreSlim? _receiveSemaphore;
        private static readonly object _stopSendingFrame = new();

        public override void EnableReceiveFlowControl()
        {
            // Nothing to do.
        }

        public override void EnableSendFlowControl()
        {
            // If we are going to send stream data, we create a send semaphore and send it to the peer's
            // stream. The semaphore is used to ensure the SendAsync call blocks until the peer received
            // the data.
            Debug.Assert(_sendSemaphore == null);
            _sendSemaphore = new SemaphoreSlim(0);
            _connection.SendFrameAsync(this, frame: _sendSemaphore, endStream: false, cancel: default).AsTask();
        }

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            // If we couldn't get data from the previously received buffers, wait for additional data to be
            // received and fill the buffer with the received data.
            if (_receivedPos.BufferIndex == _receivedBuffers.Length)
            {
                (object frame, bool endStream) = await WaitAsync(cancel).ConfigureAwait(false);
                if (frame is ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
                {
                    _receivedBuffers = buffers;
                    _receivedPos = (0, 0);
                    _receivedEndStream = endStream;
                }
                else
                {
                    Debug.Assert(false, $"unexpected frame {frame}");
                }
            }

            try
            {
                if (_receivedBuffers.Length == 0)
                {
                    return 0;
                }

                Debug.Assert(_receivedPos.BufferIndex < _receivedBuffers.Length);
                int offset = 0;
                while (offset < buffer.Length)
                {
                    Debug.Assert(_receivedPos.Offset <= _receivedBuffers.Span[_receivedPos.BufferIndex].Length);

                    ReadOnlyMemory<byte> receiveBuffer =
                        _receivedBuffers.Span[_receivedPos.BufferIndex][_receivedPos.Offset..];
                    int remaining = buffer.Length - offset;
                    if (remaining < receiveBuffer.Length)
                    {
                        receiveBuffer[0..remaining].CopyTo(buffer[offset..]);
                        _receivedPos.Offset += remaining;
                        offset += remaining;
                    }
                    else
                    {
                        receiveBuffer.CopyTo(buffer[offset..]);
                        offset += receiveBuffer.Length;
                        if (++_receivedPos.BufferIndex == _receivedBuffers.Length)
                        {
                            // No more data available from the received buffers.
                            break;
                        }
                        _receivedPos.Offset = 0;
                    }
                }
                return offset;
            }
            finally
            {
                // If all the buffered data has been consumed, release the semaphore to let the sender send
                // more data and if the end stream is reached, we complete the reads to eventually shutdown
                // the stream.
                if (_receivedPos.BufferIndex == _receivedBuffers.Length)
                {
                    try
                    {
                        _receiveSemaphore?.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore, expected if the sender called Disposed on the semaphore.
                    }

                    if (_receivedEndStream)
                    {
                        TrySetReadCompleted();
                    }
                }
            }
        }

        public override async ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            if (WriteCompleted)
            {
                throw new StreamAbortedException(StreamError.StreamAborted);
            }

            await _connection.SendFrameAsync(this, buffers, endStream, cancel).ConfigureAwait(false);

            if (_sendSemaphore != null)
            {
                await _sendSemaphore.WaitAsync(cancel).ConfigureAwait(false);
            }
        }

        public override string ToString()
        {
            int requestID = Id % 4 < 2 ? (int)(Id >> 2) + 1 : 0;
            return $"ID = {requestID} {(requestID == 0 ? "oneway" : "twoway")}";
        }

        protected override void Shutdown()
        {
            base.Shutdown();
            _connection.ReleaseStream(this);
            _sendSemaphore?.Dispose();
        }

        /// <summary>Constructor for incoming colocated stream</summary>
        internal ColocStream(ColocConnection connection, long streamId)
            : base(connection, streamId) => _connection = connection;

        /// <summary>Constructor for outgoing colocated stream</summary>
        internal ColocStream(ColocConnection connection, bool bidirectional)
            : base(connection, bidirectional) => _connection = connection;

        internal void ReceivedFrame(object frame, bool endStream)
        {
            if (frame is StreamError errorCode)
            {
                // An error code indicates a reset frame.

                // It's important to set the exception before completing the reads because ReceiveAsync
                // expects the exception to be set if reads are completed.
                SetException(new StreamAbortedException(errorCode));

                // Cancel the dispatch source before completing reads otherwise the source might be disposed
                // after and the dispatch won't be canceled.
                try
                {
                    CancelDispatchSource?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Expected if the stream is already shutdown.
                }

                TrySetReadCompleted();
            }
            else if (frame == _stopSendingFrame)
            {
                // Stop sending frame, complete the writes to stop sending data.
                TrySetWriteCompleted();
            }
            else if (frame is SemaphoreSlim semaphore)
            {
                // Flow control semaphore to ensure the sender waits for the data to be received before sending
                // more data.
                _receiveSemaphore = semaphore;
            }
            else
            {
                // Stream frame, queue it for ReceiveAsync
                Debug.Assert(frame is ReadOnlyMemory<ReadOnlyMemory<byte>>);
                QueueResult((frame, endStream));
            }
        }

        private protected override Task SendResetFrameAsync(StreamError errorCode) =>
            _ = _connection.SendFrameAsync(this, frame: errorCode, endStream: true, default).AsTask();

        private protected override Task SendStopSendingFrameAsync(StreamError errorCode) =>
            _ = _connection.SendFrameAsync(this, frame: _stopSendingFrame, endStream: false, default).AsTask();
    }
}
