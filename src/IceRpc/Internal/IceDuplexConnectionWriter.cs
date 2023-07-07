// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal;

/// <summary>A helper class to write data to a duplex connection. It provides a PipeWriter-like API but is not a
/// PipeWriter. Like a PipeWriter, its methods shouldn't be called concurrently.</summary>
internal class IceDuplexConnectionWriter : IBufferWriter<byte>, IDisposable
{
    public long UnflushedBytes => _pipe.Writer.UnflushedBytes;

    private readonly IDuplexConnection _connection;
    private readonly Pipe _pipe;
    private readonly SequenceCoupler _sequenceCoupler = new();

    public void Advance(int bytes) => _pipe.Writer.Advance(bytes);

    public void Dispose()
    {
        _pipe.Writer.Complete();
        _pipe.Reader.Complete();
    }

    public Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

    public Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

    /// <summary>Constructs a duplex connection writer.</summary>
    /// <param name="connection">The duplex connection to write to.</param>
    /// <param name="pool">The memory pool to use.</param>
    /// <param name="minimumSegmentSize">The minimum segment size for buffers allocated from <paramref name="pool"/>.
    /// </param>
    internal IceDuplexConnectionWriter(IDuplexConnection connection, MemoryPool<byte> pool, int minimumSegmentSize)
    {
        _connection = connection;

        // The readerScheduler doesn't matter (we don't call _pipe.Reader.ReadAsync) and the writerScheduler doesn't
        // matter (_pipe.Writer.FlushAsync never blocks).
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            useSynchronizationContext: false));
    }

    /// <summary>Flush the buffered data.</summary>
    internal ValueTask FlushAsync(CancellationToken cancellationToken) =>
        WriteAsync(ReadOnlySequence<byte>.Empty, cancellationToken);

    /// <summary>Writes a sequence of bytes.</summary>
    internal async ValueTask WriteAsync(ReadOnlySequence<byte> source, CancellationToken cancellationToken)
    {
        // Ice only calls WriteAsync or FlushAsync with unflushed bytes.
        Debug.Assert(_pipe.Writer.UnflushedBytes > 0);

        _ = await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        _pipe.Reader.TryRead(out ReadResult readResult);
        Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled);

        try
        {
            await _connection.WriteAsync(
                _sequenceCoupler.Concat(readResult.Buffer, source),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pipe.Reader.AdvanceTo(readResult.Buffer.End);
        }
    }
}
