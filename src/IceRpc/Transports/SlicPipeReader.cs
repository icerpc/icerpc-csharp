// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace IceRpc.Transports.Internal
{
    internal class SlicPipeReader : PipeReader, IAsyncQueueValueTaskSource<bool>
    {
        // TODO: remove pragma warning disable/restore once analyser is fixed.
        // It is necessary to call new() explicitly to execute the parameterless ctor of AsyncQueueCore, which is
        // synthesized from AsyncQueueCore fields defaults.
#pragma warning disable CA1805 // member is explicitly initialized to its default value
        private AsyncQueueCore<bool> _queue = new();
#pragma warning restore CA1805
        private CircularBuffer _buffer;
        // The receive credit. This is the amount of data received from the peer that we didn't acknowledge as
        // received yet. Once the credit reach a given threshold, we'll notify the peer with a StreamConsumed
        // frame data has been consumed and additional credit is therefore available for sending.
        private int _receiveCredit;
        private bool _receivedEndStream;
        private readonly SlicMultiplexedStream _stream;

        public override bool TryRead(out ReadResult result)
        {
            if (_buffer.IsEmpty)
            {
                result = new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, _stream.ReadsCompleted);
                return false;
            }
            else
            {
                // TODO: isCompleted?
                result = new ReadResult(_buffer.GetReadBuffer(), isCanceled: false, isCompleted: _receivedEndStream);
                return true;
            }
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancel = default)
        {
            if (_stream.ReadsCompleted)
            {
                if (_stream.ResetErrorCode is byte errorCode)
                {
                    throw new MultiplexedStreamAbortedException(errorCode);
                }
                return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);
            }

            if (_buffer.IsEmpty)
            {
                _receivedEndStream = await _queue.DequeueAsync(this, cancel).ConfigureAwait(false);

                if (_buffer.IsEmpty)
                {
                    Debug.Assert(_stream.ReadsCompleted);
                    if (_stream.ResetErrorCode is byte errorCode)
                    {
                        throw new MultiplexedStreamAbortedException(errorCode);
                    }
                    _stream.TrySetReadCompleted();
                    return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);
                }
            }

            return new ReadResult(_buffer.GetReadBuffer(), isCanceled: false, isCompleted: _receivedEndStream);
        }

        public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            // TODO: XXX add support for examined position!

            int consumedLength = _buffer.AdvanceTo(consumed);

            int credit = Interlocked.Add(ref _receiveCredit, consumedLength);
            if (credit >= _buffer.Capacity * 0.5)
            {
                // Reset _receiveCredit before notifying the peer that it can send more data.
                Interlocked.Exchange(ref _receiveCredit, 0);

                // Notify the peer that it can send additional data.
                _stream.SendStreamConsumed(credit);
            }

            if (_buffer.IsEmpty && _receivedEndStream)
            {
                _stream.TrySetReadCompleted();
            }
        }

        public override void CancelPendingRead() => _queue.Enqueue(true);

        public override void Complete(Exception? exception = null)
        {
            if (!_stream.ReadsCompleted)
            {
                if (exception is null)
                {
                    _stream.TrySetReadCompleted();
                }
                else if (exception is MultiplexedStreamAbortedException abortedException)
                {
                    _stream.AbortRead(abortedException.ErrorCode);
                }
                else
                {
                    throw new InvalidOperationException("unexpected Complete exception", exception);
                }
            }

            _buffer.Dispose();
        }

        internal SlicPipeReader(SlicMultiplexedStream stream, int maxSize)
        {
            _stream = stream;
            _buffer = new CircularBuffer(maxSize);
        }

        internal Memory<byte> GetWriteBuffer(int size) => _buffer.GetWriteBuffer(size);

        internal void FlushWrite(bool endStream) => _queue.Enqueue(endStream);

        bool IValueTaskSource<bool>.GetResult(short token) => _queue.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _queue.GetStatus(token);

        void IValueTaskSource<bool>.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags) => _queue.OnCompleted(continuation, state, token, flags);

        void IAsyncQueueValueTaskSource<bool>.Cancel() => _queue.TryComplete(new OperationCanceledException());
    }
}
