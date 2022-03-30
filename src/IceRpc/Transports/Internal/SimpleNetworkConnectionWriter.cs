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
        private int _state;

        /// <inheritdoc/>
        public void Advance(int bytes) => _pipe.Writer.Advance(bytes);

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_state.TrySetFlag(State.Disposed))
            {
                // If the pipe is being used for reading we can't call CompleteAsync since it's not safe. We call
                // CancelPendingRead instead, the implementation will complete the pipe reader/writer once it's no
                // longer writing.
                if (_state.HasFlag(State.Writing))
                {
                    _pipe.Reader.CancelPendingRead();
                }
                else
                {
                    _pipe.Writer.Complete();
                    _pipe.Reader.Complete();
                }
            }
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
        internal ValueTask FlushAsync(CancellationToken cancel) =>
            WriteAsync(ReadOnlySequence<byte>.Empty, ReadOnlySequence<byte>.Empty, cancel);

        /// <summary>Writes a sequence of bytes.</summary>
        internal ValueTask WriteAsync(ReadOnlySequence<byte> source, CancellationToken cancel) =>
            WriteAsync(source, ReadOnlySequence<byte>.Empty, cancel);

        /// <summary>Writes two sequences of bytes.</summary>
        internal async ValueTask WriteAsync(
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            CancellationToken cancel)
        {
            if (!_state.TrySetFlag(State.Writing))
            {
                throw new InvalidOperationException("writing is not thread safe");
            }

            try
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
                    _pipe.Reader.TryRead(out ReadResult readResult);

                    if (readResult.IsCanceled)
                    {
                        throw new ObjectDisposedException($"{typeof(SimpleNetworkConnectionWriter)}");
                    }
                    Debug.Assert(!readResult.IsCompleted);

                    consumed = readResult.Buffer.GetPosition(readResult.Buffer.Length);
                    AddToSendBuffers(readResult.Buffer);
                }

                // Next add the data from source1 and source2.
                AddToSendBuffers(source1);
                AddToSendBuffers(source2);

                try
                {
                    ValueTask task = _connection.WriteAsync(_sendBuffers, cancel);
                    if (task.IsCompleted)
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
            }
            catch
            {
                if (_state.HasFlag(State.Disposed))
                {
#pragma warning disable CA1849
                    _pipe.Reader.Complete();
                    _pipe.Writer.Complete();
#pragma warning restore CA1849
                }
                throw;
            }
            finally
            {
                _state.ClearFlag(State.Writing);
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

        private enum State : int
        {
            Disposed = 1,
            Writing = 2,
        }
    }
}
