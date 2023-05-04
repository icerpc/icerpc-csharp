// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>A helper class to write data to a duplex connection. It provides a PipeWriter-like API but is not a
/// PipeWriter. The data written to this writer is copied and buffered with an internal pipe. The data from the pipe is
/// written on the duplex connection with a background task. This allows prompt cancellation of writes and improves
/// write concurrency since multiple writes can be buffered and sent with a single <see
/// cref="IDuplexConnection.WriteAsync" /> call.</summary>
internal class DuplexConnectionWriter : IBufferWriter<byte>, IAsyncDisposable
{
    private Task? _backgroundWriteTask;
    private readonly IDuplexConnection _connection;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly int _maxWriteSize;
    private readonly object _mutex = new();
    private readonly Pipe _pipe;
    private readonly List<ReadOnlyMemory<byte>> _segments = new() { ReadOnlyMemory<byte>.Empty };

    /// <inheritdoc/>
    public void Advance(int bytes) => _pipe.Writer.Advance(bytes);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        lock (_mutex)
        {
            // Make sure that the background task is not started in case WriteAsync is called concurrently.
            _backgroundWriteTask ??= Task.CompletedTask;
        }

        await _backgroundWriteTask.ConfigureAwait(false);

        _pipe.Reader.Complete();
        _pipe.Writer.Complete();

        _disposeCts.Dispose();
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

    /// <summary>Constructs a duplex connection writer.</summary>
    /// <param name="connection">The duplex connection to write to.</param>
    /// <param name="pool">The memory pool to use.</param>
    /// <param name="maxWriteSize">The maximum size of data to write on the duplex connection.</param>
    /// <param name="maxBufferSize">The maximum size of data to buffer.</param>
    internal DuplexConnectionWriter(
        IDuplexConnection connection,
        MemoryPool<byte> pool,
        int maxWriteSize,
        int maxBufferSize)
    {
        _connection = connection;
        _maxWriteSize = maxWriteSize;

        // The segment size allocated by the pool is set to the maximum write size. This ensures that sequence elements
        // returned from the reader will be up to this size given that we never request larger segments from the writer.
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: maxWriteSize,
            resumeWriterThreshold: maxWriteSize,
            pauseWriterThreshold: maxBufferSize));
    }

    internal void Complete() => _pipe.Writer.Complete();

    internal ValueTask FlushAsync(CancellationToken cancellationToken) =>
        WriteAsync(ReadOnlySequence<byte>.Empty, ReadOnlySequence<byte>.Empty, cancellationToken);

    internal ValueTask WriteAsync(ReadOnlySequence<byte> source, CancellationToken cancellationToken) =>
        WriteAsync(source, ReadOnlySequence<byte>.Empty, cancellationToken);

    /// <summary>Writes two sequences of bytes.</summary>
    internal async ValueTask WriteAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        CancellationToken cancellationToken)
    {
        if (_backgroundWriteTask is null)
        {
            // Start the background task if it's not already started.
            lock (_mutex)
            {
                _backgroundWriteTask ??= Task.Run(BackgroundWritesAsync, CancellationToken.None);
            }
        }

        if (source1.Length > 0)
        {
            Write(source1);
        }
        if (source2.Length > 0)
        {
            Write(source2);
        }

        await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        void Write(ReadOnlySequence<byte> sequence)
        {
            foreach (ReadOnlyMemory<byte> buffer in sequence)
            {
                ReadOnlyMemory<byte> source = buffer;
                while (source.Length > 0)
                {
                    Memory<byte> destination = _pipe.Writer.GetMemory();
                    if (destination.Length < source.Length)
                    {
                        source[0..destination.Length].CopyTo(destination);
                        _pipe.Writer.Advance(destination.Length);
                        source = source[destination.Length..];
                    }
                    else
                    {
                        source.CopyTo(destination);
                        _pipe.Writer.Advance(source.Length);
                        source = ReadOnlyMemory<byte>.Empty;
                    }
                }
            }
        }
    }

    private async Task BackgroundWritesAsync()
    {
        try
        {
            while (true)
            {
                ReadResult readResult = await _pipe.Reader.ReadAsync(_disposeCts.Token).ConfigureAwait(false);

                if (readResult.Buffer.Length > 0)
                {
                    // TODO: change the IDuplexConnection.WriteAsync API to use ReadOnlySequence<byte> instead.
                    int size = Math.Min(readResult.Buffer.First.Length, _maxWriteSize);
                    ReadOnlyMemory<byte> buffer = readResult.Buffer.First[0..size];
                    _segments[0] = buffer;
                    await _connection.WriteAsync(_segments, _disposeCts.Token).ConfigureAwait(false);
                    _pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(buffer.Length));
                }

                if (readResult.IsCompleted)
                {
                    await _connection.ShutdownWriteAsync(_disposeCts.Token).ConfigureAwait(false);
                    break;
                }
            }
            _pipe.Reader.Complete();
        }
        catch (Exception exception)
        {
            _pipe.Reader.Complete(exception);
        }
    }
}
