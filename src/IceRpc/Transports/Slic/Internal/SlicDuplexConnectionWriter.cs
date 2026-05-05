// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>A helper class to write data to a duplex connection. Its methods shouldn't be called concurrently. The data
/// written to this writer is copied and buffered with an internal pipe. The data from the pipe is written on the duplex
/// connection with a background task.</summary>
internal class SlicDuplexConnectionWriter : IBufferWriter<byte>, IAsyncDisposable
{
    internal Task WriterTask { get; private init; }

    private readonly IDuplexConnection _connection;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _disposeTask;
    private readonly Pipe _pipe;

    public void Advance(int bytes) => _pipe.Writer.Advance(bytes);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposeTask ??= PerformDisposeAsync();
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            _disposeCts.Cancel();

            await WriterTask.ConfigureAwait(false);

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
    /// <param name="pauseWriterThreshold">The pipe pause writer threshold. When buffered data exceeds this value, <see
    /// cref="FlushAsync" /> blocks until the background writer task drains enough data from the pipe.</param>
    internal SlicDuplexConnectionWriter(
        IDuplexConnection connection,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        int pauseWriterThreshold)
    {
        _connection = connection;

        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: pauseWriterThreshold,
            // When pauseWriterThreshold is non-zero, resume writes once buffered data drops to half of the threshold.
            // Without this override, the PipeOptions default of 32 KB would exceed pauseWriterThreshold for small
            // thresholds and throw. When pauseWriterThreshold is 0, leave both at 0 to disable pausing.
            resumeWriterThreshold: pauseWriterThreshold == 0 ? 0 : pauseWriterThreshold / 2,
            useSynchronizationContext: false));

        WriterTask = Task.Run(
            async () =>
            {
                try
                {
                    while (true)
                    {
                        ReadResult readResult = await _pipe.Reader.ReadAsync(_disposeCts.Token).ConfigureAwait(false);

                        if (!readResult.Buffer.IsEmpty)
                        {
                            await _connection.WriteAsync(readResult.Buffer, _disposeCts.Token).ConfigureAwait(false);
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

    /// <summary>Flushes the underlying pipe. May block when the buffered data exceeds the configured pause writer
    /// threshold.</summary>
    internal ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
        _pipe.Writer.FlushAsync(cancellationToken);

    /// <summary>Requests the shut down of the duplex connection writes after the buffered data is written on the
    /// duplex connection.</summary>
    internal void Shutdown() =>
        // Completing the pipe writer makes the background write task complete successfully.
        _pipe.Writer.Complete();
}
