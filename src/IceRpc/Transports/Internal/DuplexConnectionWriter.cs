// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>A helper class to write data to a duplex connection. It provides a PipeWriter-like API but is not a
/// PipeWriter. Like a PipeWriter, its methods shouldn't be called concurrently. The data written to this writer is
/// copied and buffered with an internal pipe. The data from the pipe is written on the duplex connection with a
/// background task. This allows prompt cancellation of writes and improves write concurrency since multiple writes can
/// be buffered and sent with a single <see cref="IDuplexConnection.WriteAsync" /> call.</summary>
internal class DuplexConnectionWriter : IBufferWriter<byte>, IAsyncDisposable
{
    private readonly Task _backgroundWriteTask;
    private readonly IDuplexConnection _connection;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _disposeTask;
    private readonly Pipe _pipe;
    // This field is temporary and will be removed once IDuplexConnection.WriteAsync no longer requires an
    // IReadOnlyList<ReadOnlyMemory<byte> parameter.
    private readonly List<ReadOnlyMemory<byte>> _segments = new() { ReadOnlyMemory<byte>.Empty };

    /// <inheritdoc/>
    public void Advance(int bytes) => _pipe.Writer.Advance(bytes);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposeTask ??= PerformDisposeAsync();
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            _disposeCts.Cancel();

            await _backgroundWriteTask.ConfigureAwait(false);

            _pipe.Reader.Complete();
            _pipe.Writer.Complete();

            _disposeCts.Dispose();
        }
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0) => _pipe.Writer.GetMemory(sizeHint);

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0) => _pipe.Writer.GetSpan(sizeHint);

    /// <summary>Constructs a duplex connection writer.</summary>
    /// <param name="connection">The duplex connection to write to.</param>
    /// <param name="pool">The memory pool to use.</param>
    /// <param name="minimumSegmentSize">The minimum segment size for buffers allocated from <paramref
    /// name="pool"/>.</param>
    /// <param name="pauseWriterThreshold">The number of buffered data in bytes when <see cref="FlushAsync" /> or <see
    /// cref="WriteAsync(ReadOnlySequence{byte}, CancellationToken)" /> starts blocking. A value of <c>0</c>, prevents
    /// these calls from blocking.</param>
    /// <param name="resumeWriterThreshold">The number of free buffer space in bytes when <see cref="FlushAsync" /> or
    /// <see cref="WriteAsync(ReadOnlySequence{byte}, CancellationToken)" /> stops blocking.</param>
    internal DuplexConnectionWriter(
        IDuplexConnection connection,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        int pauseWriterThreshold,
        int resumeWriterThreshold)
    {
        _connection = connection;
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: pauseWriterThreshold,
            resumeWriterThreshold: resumeWriterThreshold));

        _backgroundWriteTask = Task.Run(
            async () =>
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
            });
    }

    internal ValueTask FlushAsync(CancellationToken cancellationToken) =>
        WriteAsync(ReadOnlySequence<byte>.Empty, ReadOnlySequence<byte>.Empty, cancellationToken);

    /// <summary>Requests the shut down of the duplex connection after the buffered data is written on the duplex
    /// connection.</summary>
    internal void Shutdown() => _pipe.Writer.Complete();

    internal ValueTask WriteAsync(ReadOnlySequence<byte> source, CancellationToken cancellationToken) =>
        WriteAsync(source, ReadOnlySequence<byte>.Empty, cancellationToken);

    /// <summary>Writes two sequences of bytes.</summary>
    internal async ValueTask WriteAsync(
        ReadOnlySequence<byte> source1,
        ReadOnlySequence<byte> source2,
        CancellationToken cancellationToken)
    {
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
}
