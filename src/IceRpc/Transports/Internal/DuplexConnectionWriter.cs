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
    /// <param name="pauseWriterThreshold">The number of buffered data in bytes when <see cref="FlushAsync" /> or <see
    /// cref="WriteAsync(ReadOnlySequence{byte}, CancellationToken)" /> starts blocking. A value of <c>0</c>, prevents
    /// these calls from blocking.</param>
    /// <param name="resumeWriterThreshold">The number of free buffer space in bytes when <see cref="FlushAsync" /> or
    /// <see cref="WriteAsync(ReadOnlySequence{byte}, CancellationToken)" /> stops blocking.</param>
    internal DuplexConnectionWriter(
        IDuplexConnection connection,
        MemoryPool<byte> pool,
        int pauseWriterThreshold = 0,
        int resumeWriterThreshold = 0)
    {
        _connection = connection;

        // The segment size allocated by the pipe is set to 16KB. This ensures that the size of sequence elements
        // returned by the pipe won't be larger since we never provide a size hint to PipeWriter.GetMemory(). It's set
        // to the maximum SSL record size to avoid the SSL stream from having to split the data for encryption algorithm
        // whose encrypted data size is the same as the input data size.
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: 16384,
            pauseWriterThreshold: pauseWriterThreshold,
            resumeWriterThreshold: resumeWriterThreshold));
    }

    internal ValueTask FlushAsync(CancellationToken cancellationToken) =>
        WriteAsync(ReadOnlySequence<byte>.Empty, ReadOnlySequence<byte>.Empty, cancellationToken);

    /// <summary>Shuts down the duplex connection. The background task will take care of it once a read returns a
    /// completed result.</summary>
    internal void Shutdown() => _pipe.Writer.Complete();

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
                    _segments.Clear();
                    foreach (ReadOnlyMemory<byte> segment in readResult.Buffer)
                    {
                        _segments.Add(segment);
                    }

                    // TODO: change the IDuplexConnection.WriteAsync API to use ReadOnlySequence<byte> instead.
                    await _connection.WriteAsync(_segments, _disposeCts.Token).ConfigureAwait(false);
                    _pipe.Reader.AdvanceTo(readResult.Buffer.End);
                }

                if (readResult.IsCompleted)
                {
                    await _connection.ShutdownWriteAsync(_disposeCts.Token).ConfigureAwait(false);
                    break;
                }
            }
            _pipe.Reader.Complete();
        }
        catch (OperationCanceledException)
        {
            // DisposeAsync was called.
        }
        catch (Exception exception)
        {
            _pipe.Reader.Complete(exception);
        }
    }
}
