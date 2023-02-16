// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>A helper class to write data to a duplex connection. It provides a PipeWriter-like API but is not a
/// PipeWriter.</summary>
internal class DuplexConnectionWriter : IBufferWriter<byte>, IDisposable
{
    internal IDuplexConnection DuplexConnection { get; set; }

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

    /// <summary>Constructs a duplex connection writer.</summary>
    /// <param name="connection">The duplex connection to write to.</param>
    /// <param name="pool">The memory pool to use.</param>
    /// <param name="minimumSegmentSize">The minimum segment size for buffers allocated from <paramref name="pool"/>.
    /// </param>
    internal DuplexConnectionWriter(IDuplexConnection connection, MemoryPool<byte> pool, int minimumSegmentSize)
    {
        DuplexConnection = connection;
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            writerScheduler: PipeScheduler.Inline));
    }

    /// <summary>Flush the buffered data.</summary>
    internal ValueTask FlushAsync(CancellationToken cancellationToken) =>
        WriteAsync(ReadOnlySequence<byte>.Empty, ReadOnlySequence<byte>.Empty, cancellationToken);

    /// <summary>Writes a sequence of bytes.</summary>
    internal ValueTask WriteAsync(ReadOnlySequence<byte> source, CancellationToken cancellationToken) =>
        WriteAsync(source, ReadOnlySequence<byte>.Empty, cancellationToken);

    /// <summary>Writes two sequences of bytes.</summary>
    internal async ValueTask WriteAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        CancellationToken cancellationToken)
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
            await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
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
            ValueTask task = DuplexConnection.WriteAsync(_sendBuffers, cancellationToken);
            if (cancellationToken.CanBeCanceled && !task.IsCompleted)
            {
                await task.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException exception)
        {
            throw new IceRpcException(
                IceRpcError.OperationAborted,
                "The write operation was aborted by the disposal of the duplex connection.",
                exception);
        }
        finally
        {
            if (consumed is not null)
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
