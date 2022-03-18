// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>A pipe writer implementation to write data over a simple network connection. Writing one the pipe
    /// writer is not supported since it would lead to sending partial data that can't be processed by the
    /// peer.</summary>
    internal class SimpleNetworkConnectionPipeWriter : PipeWriter
    {
        private readonly ISimpleNetworkConnection _connection;
        private readonly Pipe _pipe;
        private readonly List<ReadOnlyMemory<byte>> _sendBuffers = new(16);

        /// <inheritdoc/>
        public override void CancelPendingFlush()
        {
            // We ignore the cancellation request since we don't want to send partial data.
        }

        /// <inheritdoc/>
        public override void Complete(Exception? exception = null)
        {
            _pipe.Writer.Complete(exception);
            _pipe.Reader.Complete(exception);
        }

        /// <inheritdoc/>
        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancel = default) =>
            WriteAsync(ReadOnlyMemory<byte>.Empty, cancel);

        /// <inheritdoc/>
        public override void Advance(int bytes) => _pipe.Writer.Advance(bytes);

        /// <inheritdoc/>
        public override Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

        /// <inheritdoc/>
        public override Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

        /// <inheritdoc/>
        public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken _)
        {
            await WriteAsync(new ReadOnlySequence<byte>(source), ReadOnlySequence<byte>.Empty).ConfigureAwait(false);
            return new FlushResult();
        }

        internal SimpleNetworkConnectionPipeWriter(
            ISimpleNetworkConnection connection,
            MemoryPool<byte> pool,
            int minimumSegmentSize)
        {
            _connection = connection;
            _pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));
        }

        internal ValueTask WriteAsync(ReadOnlySequence<byte> source, CancellationToken _) =>
            WriteAsync(source, ReadOnlySequence<byte>.Empty);

        internal async ValueTask WriteAsync(ReadOnlySequence<byte> source1, ReadOnlySequence<byte> source2)
        {
            if (_pipe.Writer.UnflushedBytes == 0 && source1.IsEmpty && source2.IsEmpty)
            {
                return;
            }

            _sendBuffers.Clear();

            // First add the data from the internal pipe.
            SequencePosition consumed = default;
            if (_pipe.Writer.UnflushedBytes > 0)
            {
                await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                if (_pipe.Reader.TryRead(out ReadResult readResult) && !readResult.IsCompleted)
                {
                    consumed = readResult.Buffer.GetPosition(readResult.Buffer.Length);
                    AddToSendBuffers(readResult.Buffer);
                }
            }

            // Next add the data from source1 and source2.
            AddToSendBuffers(source1);
            AddToSendBuffers(source2);

            try
            {
                ValueTask task = _connection.WriteAsync(_sendBuffers, CancellationToken.None);
                if (task.IsCompleted)
                {
                    await task.ConfigureAwait(false);
                }
                else
                {
                    await task.AsTask().WaitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                _pipe.Reader.AdvanceTo(consumed);
            }

            void AddToSendBuffers(ReadOnlySequence<byte> source)
            {
                if (source.IsEmpty)
                {
                    // Nothing to add.
                }
                else if (source.IsSingleSegment)
                {
                    _sendBuffers.Add(source.First);
                }
                else
                {
                    foreach (ReadOnlyMemory<byte> memory in source)
                    {
                        _sendBuffers.Add(memory);
                    }
                }
            }
        }
    }
}
