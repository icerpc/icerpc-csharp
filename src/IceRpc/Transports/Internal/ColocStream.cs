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
        private (int Segment, int Offset) _receivedPos;
        private bool _receivedEndStream;
        private readonly ColocConnection _connection;
        private SemaphoreSlim? _sendSemaphore;
        private SemaphoreSlim? _receiveSemaphore;

        public override void AbortRead(RpcStreamError errorCode)
        {
            // It's important to set the exception before completing the reads because ReceiveAsync expects the
            // exception to be set if reads are completed.
            SetException(new RpcStreamAbortedException(errorCode));

            if (TrySetReadCompleted(shutdown: false))
            {
                // Send stop sending frame before shutting down.
                // TODO

                // Shutdown the stream if not already done.
                TryShutdown();
            }
        }

        public override void AbortWrite(RpcStreamError errorCode)
        {
            // Notify the peer of the abort if the stream or connection is not aborted already.
            if (!IsShutdown && errorCode != RpcStreamError.ConnectionAborted)
            {
                _ = _connection.SendFrameAsync(
                    this,
                    frame: errorCode,
                    endStream: true,
                    CancellationToken.None).AsTask();
            }

            if (TrySetWriteCompleted(shutdown: false))
            {

                // Shutdown the stream if not already done.
                TryShutdown();
            }
        }

        public override void EnableReceiveFlowControl()
        {
            // Nothing to do.
        }

        public override void EnableSendFlowControl()
        {
            // If we are going to send stream data, we create a send semaphore and send it to the peer's
            // stream. The semaphore is used to ensure the SendAsync call blocks until the peer received
            // the data.
            _sendSemaphore = new SemaphoreSlim(0);
            _connection.SendFrameAsync(this, frame: _sendSemaphore, endStream: false, cancel: default).AsTask();
        }

        public override async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            // If the receive semaphore isn't set yet, get it from the channel, the semaphore is sent before
            // stream data.
            object frame;
            if (_receiveSemaphore == null)
            {
                (frame, _receivedEndStream) = await WaitAsync(cancel).ConfigureAwait(false);
                Debug.Assert(!_receivedEndStream);
                _receiveSemaphore = frame as SemaphoreSlim;
                Debug.Assert(_receiveSemaphore != null);
                _connection.FinishedReceivedFrame();
            }

            // If there's still received buffered data, first consume it.
            if (_receivedPos.Segment < _receivedBuffers.Length)
            {
                int received = ReceiveFromBuffer(buffer);
                if (received > 0)
                {
                    // If we consumed some data, that's good enough, return.
                    return received;
                }
            }

            if (ReadCompleted)
            {
                return 0;
            }

            // If we couldn't get data from the previously received buffers, wait for additional data to be received
            // and fill the buffer with the received data.
            (frame, _receivedEndStream) = await WaitAsync(cancel).ConfigureAwait(false);
            _receivedBuffers = (ReadOnlyMemory<ReadOnlyMemory<byte>>)frame;
            _receivedPos = (0, 0);

            Debug.Assert (!_receivedBuffers.IsEmpty);

            return ReceiveFromBuffer(buffer);

            int ReceiveFromBuffer(Memory<byte> buffer)
            {
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

                // If all the buffered data has been consumed, release the semaphore to let the sender send more data.
                if (_receivedPos.Segment == _receivedBuffers.Length)
                {
                    try
                    {
                        _receiveSemaphore.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                // If we received the end stream flag, we won't receive any additional data, complete the stream reads.
                if (_receivedEndStream)
                {
                    TrySetReadCompleted();
                }
                return offset;
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

            if (endStream)
            {
                TrySetWriteCompleted();
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
                // It's important to set the exception before completing the reads because ReceiveAsync expects the
                // exception to be set if reads are completed.
                SetException(new RpcStreamAbortedException(errorCode));

                // Cancel the dispatch source before completing reads otherwise the source might be disposed after.
                CancelDispatchSource?.Cancel();

                TrySetReadCompleted();

                _connection.FinishedReceivedFrame();
            }
            else if (_receiveSemaphore == null)
            {
                QueueResult((frame, endStream));
            }
            else
            {
                QueueResult((frame, endStream));
                _connection.FinishedReceivedFrame();
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
            (object frame, bool endStream) = await WaitFrameAsync(cancel).ConfigureAwait(false);
            if (frame is ReadOnlyMemory<ReadOnlyMemory<byte>> data)
            {
                // Initialize or GoAway frame.
                if (_connection.Protocol == Protocol.Ice1)
                {
                    Debug.Assert(expectedFrameType == data.Span[0].Span[8]);
                    return Memory<byte>.Empty;
                }
                else
                {
                    Debug.Assert(expectedFrameType == data.Span[0].Span[0]);
                    (int size, int sizeLength) = data.Span[0].Span[1..].DecodeSize20();

                    // TODO: why are we returning only the first buffer?
                    return data.Span[0].Slice(1 + sizeLength, size);
                }
            }
            else
            {
                Debug.Assert(false);
                throw new InvalidDataException("unexpected frame");
            }
        }

        private protected override async ValueTask SendFrameAsync(OutgoingFrame frame, CancellationToken cancel) =>
            await _connection.SendFrameAsync(
                this,
                frame.ToIncoming(),
                endStream: frame.StreamWriter == null,
                cancel).ConfigureAwait(false);

        private async ValueTask<(object frameObject, bool endStream)> WaitFrameAsync(CancellationToken cancel)
        {
            (object frameObject, bool endStream) = await WaitAsync(cancel).ConfigureAwait(false);
            if (ReadCompleted || (endStream && !TrySetReadCompleted()))
            {
                _connection.FinishedReceivedFrame();
                throw AbortException ?? new InvalidOperationException("stream receive is completed");
            }

            // Notify the connection that the frame has been processed. This must be done after completing reads
            // to ensure the stream is shutdown before. It's important to ensure the stream is removed from the
            // connection before the connection is shutdown if the next frame is a close connection frame.
            _connection.FinishedReceivedFrame();
            return (frameObject, endStream);
        }
    }
}
