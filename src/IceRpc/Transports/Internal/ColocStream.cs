// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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
        private (int Segment, int Offset) _receivedPos;
        private readonly ColocConnection _connection;
        private SemaphoreSlim? _sendSemaphore;
        private SemaphoreSlim? _receiveSemaphore;
        static private readonly object _stopSending = new();

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
            // If we couldn't get data from the previously received buffers, wait for additional data to be received
            // and fill the buffer with the received data.
            if (_receivedPos.Segment == _receivedBuffers.Length)
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

                Debug.Assert(_receivedPos.Segment < _receivedBuffers.Length);
                int offset = 0;
                while (offset < buffer.Length)
                {
                    Debug.Assert(_receivedPos.Offset <= _receivedBuffers.Span[_receivedPos.Segment].Length);

                    ReadOnlyMemory<byte> receiveBuffer =
                        _receivedBuffers.Span[_receivedPos.Segment][_receivedPos.Offset..];
                    int remaining = buffer.Length - offset;
                    if (remaining < receiveBuffer.Length)
                    {
                        receiveBuffer[0..remaining].CopyTo(buffer);
                        _receivedPos.Offset += remaining;
                        offset += remaining;
                    }
                    else
                    {
                        receiveBuffer.CopyTo(buffer);
                        offset += receiveBuffer.Length;
                        if (++_receivedPos.Segment == _receivedBuffers.Length)
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
                if (_receivedPos.Segment == _receivedBuffers.Length)
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
                throw new RpcStreamAbortedException(RpcStreamError.StreamAborted);
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
        internal ColocStream(ColocConnection connection, bool bidirectional, bool control)
            : base(connection, bidirectional, control) => _connection = connection;

        internal void ReceivedFrame(object frame, bool endStream)
        {
            if (frame is RpcStreamError errorCode)
            {
                // An error code indicates a reset frame.

                // It's important to set the exception before completing the reads because ReceiveAsync expects the
                // exception to be set if reads are completed.
                SetException(new RpcStreamAbortedException(errorCode));

                // Cancel the dispatch source before completing reads otherwise the source might be disposed after.
                CancelDispatchSource?.Cancel();

                TrySetReadCompleted();
            }
            else if (frame == _stopSending)
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
                // Stream frame, queue it for ReceiveAsync / ReceivedFrameAsync.
                Debug.Assert(frame is IncomingFrame || frame is ReadOnlyMemory<ReadOnlyMemory<byte>>);
                QueueResult((frame, endStream));
            }
        }

        internal override async ValueTask<IncomingRequest> ReceiveRequestFrameAsync(CancellationToken cancel)
        {
            (object frame, bool _) = await WaitFrameAsync(cancel).ConfigureAwait(false);
            return (IncomingRequest)frame;
        }

        internal override async ValueTask<IncomingResponse> ReceiveResponseFrameAsync(CancellationToken cancel)
        {
            (object frame, bool _) = await WaitFrameAsync(cancel).ConfigureAwait(false);
            return (IncomingResponse)frame;
        }

        private protected override async ValueTask<ReadOnlyMemory<byte>> ReceiveFrameAsync(
            byte expectedFrameType,
            CancellationToken cancel)
        {
            // This is called for receiving the Initialize or GoAway frame.
            (object frame, bool endStream) = await WaitFrameAsync(cancel).ConfigureAwait(false);

            if (frame is ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
            {
                Debug.Assert(buffers.Length == 1);
                ReadOnlyMemory<byte> buffer = buffers.Span[0];
                if (_connection.Protocol == Protocol.Ice1)
                {
                    Debug.Assert(expectedFrameType == buffer.Span[8]);
                    return Memory<byte>.Empty;
                }
                else
                {
                    Debug.Assert(expectedFrameType == buffer.Span[0]);
                    (int size, int sizeLength) = buffer.Span[1..].DecodeSize20();
                    return buffer.Slice(1 + sizeLength, size);
                }
            }
            else
            {
                Debug.Assert(false, $"unexpected frame {frame}");
                return Memory<byte>.Empty;
            }
        }

        private protected override async ValueTask SendFrameAsync(OutgoingFrame frame, CancellationToken cancel) =>
            await _connection.SendFrameAsync(
                this,
                frame.ToIncoming(),
                endStream: frame.StreamWriter == null,
                cancel).ConfigureAwait(false);

        private protected override Task SendResetFrameAsync(RpcStreamError errorCode) =>
            _ = _connection.SendFrameAsync(this, frame: errorCode, endStream: true, default).AsTask();

        private protected override Task SendStopSendingFrameAsync(RpcStreamError errorCode) =>
            _ = _connection.SendFrameAsync(this, frame: _stopSending, endStream: false, default).AsTask();

        private async ValueTask<(object frameObject, bool endStream)> WaitFrameAsync(CancellationToken cancel)
        {
            (object frameObject, bool endStream) = await WaitAsync(cancel).ConfigureAwait(false);
            if (endStream)
            {
                TrySetReadCompleted();
            }
            return (frameObject, endStream);
        }
    }
}
