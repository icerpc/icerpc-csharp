// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    internal class SimpleNetworkConnectionPipeWriter : PipeWriter
    {
        private readonly ISimpleNetworkConnection _connection;
        private readonly Pipe _pipe;
        private readonly List<ReadOnlyMemory<byte>> _sendBuffers = new(16);

        /// <inheritdoc/>
        public override void CancelPendingFlush() =>
            // Supporting this method would require to create a linked cancellation token source. Since there's no need
            // for now to support this method, we just throw.
            throw new NotImplementedException();

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
        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancel = default)
        {
            await WriteAsync(
                new ReadOnlySequence<byte>(source),
                ReadOnlySequence<byte>.Empty,
                cancel).ConfigureAwait(false);
            return default;
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

        internal ValueTask WriteAsync(ReadOnlySequence<byte> source, CancellationToken cancel) =>
            WriteAsync(source, ReadOnlySequence<byte>.Empty, cancel);

        internal async ValueTask WriteAsync(
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            CancellationToken cancel)
        {
            if (_pipe.Writer.UnflushedBytes == 0 && source1.IsEmpty && source2.IsEmpty)
            {
                return;
            }

            _sendBuffers.Clear();

            // First add the data from the internal pipe.
            SequencePosition? consumed = null;
            if (_pipe.Writer.UnflushedBytes > 0)
            {
                await _pipe.Writer.FlushAsync(cancel).ConfigureAwait(false);
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
                if (task.IsCompleted || !cancel.CanBeCanceled)
                {
                    await task.ConfigureAwait(false);
                }
                else
                {
                    await task.AsTask().WaitAsync(cancel).ConfigureAwait(false);
                }
            }
            finally
            {
                if (consumed != null)
                {
                    _pipe.Reader.AdvanceTo(consumed.Value);
                }
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
