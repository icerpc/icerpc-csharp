// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    /// <summary>A helper class to write to simple network connection. It provides a PipeWriter-like API but
    /// is not a PipeWriter.</summary>
    internal class SimpleNetworkConnectionWriter : IBufferWriter<byte>, IDisposable
    {
        private readonly ISimpleNetworkConnection _connection;
        private readonly Pipe _pipe;
        private readonly List<ReadOnlyMemory<byte>> _sendBuffers = new(16);

        /// <inheritdoc/>
        public void Advance(int bytes) => _pipe.Writer.Advance(bytes);

        /// <inheritdoc/>
        public void Dispose()
        {
            _pipe.Writer.Complete();
            _pipe.Reader.Complete();
        }

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

        /// <inheritdoc/>
        public Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

        internal SimpleNetworkConnectionWriter(
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

        /// <summary>Flush the buffered data.</summary>
        internal ValueTask FlushAsync() => WriteAsync(ReadOnlySequence<byte>.Empty, ReadOnlySequence<byte>.Empty);

        /// <summary>Writes a sequence of bytes.</summary>
        internal ValueTask WriteAsync(ReadOnlySequence<byte> source) =>
            WriteAsync(source, ReadOnlySequence<byte>.Empty);

        /// <summary>Writes two sequences of bytes.</summary>
        internal async ValueTask WriteAsync(ReadOnlySequence<byte> source1, ReadOnlySequence<byte> source2)
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
                await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                _pipe.Reader.TryRead(out ReadResult readResult);

                Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled);

                consumed = readResult.Buffer.GetPosition(readResult.Buffer.Length);
                AddToSendBuffers(readResult.Buffer);
            }

            // Next add the data from source1 and source2.
            AddToSendBuffers(source1);
            AddToSendBuffers(source2);

            try
            {
                await _connection.WriteAsync(_sendBuffers).ConfigureAwait(false);
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
