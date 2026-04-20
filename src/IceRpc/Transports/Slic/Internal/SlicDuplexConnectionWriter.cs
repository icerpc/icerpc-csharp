// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>A helper class to write data to a duplex connection. Its methods shouldn't be called concurrently. The data
/// written to this writer is copied and buffered with an internal pipe. The data from the pipe is written on the duplex
/// connection with a background task.</summary>
internal class SlicDuplexConnectionWriter : IBufferWriter<byte>, IAsyncDisposable
{
    internal Task WriterTask { get; private init; }

    private readonly ConcurrentQueue<CompletionEntry> _completionQueue = new();
    private readonly IDuplexConnection _connection;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _disposeTask;
    private readonly Pipe _pipe;

    // Tracks the absolute byte position in the pipe. Incremented by Advance, which is always called under
    // SlicConnection._mutex.
    private long _totalBytesEnqueued;

    public void Advance(int bytes)
    {
        _pipe.Writer.Advance(bytes);
        _totalBytesEnqueued += bytes;
    }

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
    internal SlicDuplexConnectionWriter(IDuplexConnection connection, MemoryPool<byte> pool, int minimumSegmentSize)
    {
        _connection = connection;

        // We set pauseWriterThreshold to 0 because per-stream local buffering is bounded by PauseWriterThreshold in
        // SlicPipeWriter, and we cannot await inside SlicConnection._mutex. The shared pipe remains an unbounded
        // synchronous staging area. The total data buffered is bounded by
        // PauseWriterThreshold * (MaxBidirectionalStreams + MaxUnidirectionalStreams) plus control frame overhead.
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            useSynchronizationContext: false));

        WriterTask = Task.Run(
            async () =>
            {
                long bytesWrittenTotal = 0;
                try
                {
                    while (true)
                    {
                        ReadResult readResult = await _pipe.Reader.ReadAsync(_disposeCts.Token).ConfigureAwait(false);

                        if (!readResult.Buffer.IsEmpty)
                        {
                            await _connection.WriteAsync(readResult.Buffer, _disposeCts.Token).ConfigureAwait(false);
                            bytesWrittenTotal += readResult.Buffer.Length;
                            _pipe.Reader.AdvanceTo(readResult.Buffer.End);

                            DrainCompletionQueue(bytesWrittenTotal);
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
                finally
                {
                    // Drain all remaining entries to unblock any waiting stream writers.
                    DrainCompletionQueue(long.MaxValue);
                }
            });
    }

    /// <summary>Enqueues a completion entry that releases per-stream local credit after the background writer has
    /// written the corresponding bytes to the duplex connection.</summary>
    /// <remarks>Must be called under SlicConnection._mutex, after writing the frame data.</remarks>
    internal void EnqueueCompletion(int creditBytes, Action<int> releaseCredit) =>
        _completionQueue.Enqueue(new CompletionEntry(_totalBytesEnqueued, creditBytes, releaseCredit));

    internal void Flush()
    {
        ValueTask<FlushResult> flushResult = _pipe.Writer.FlushAsync(CancellationToken.None);

        // PauseWriterThreshold is 0 so FlushAsync should always complete synchronously.
        Debug.Assert(flushResult.IsCompletedSuccessfully);
    }

    /// <summary>Requests the shut down of the duplex connection writes after the buffered data is written on the
    /// duplex connection.</summary>
    internal void Shutdown() =>
        // Completing the pipe writer makes the background write task complete successfully.
        _pipe.Writer.Complete();

    private void DrainCompletionQueue(long bytesWrittenTotal)
    {
        while (_completionQueue.TryPeek(out CompletionEntry entry) && entry.PipeOffset <= bytesWrittenTotal)
        {
            _completionQueue.TryDequeue(out _);
            entry.ReleaseCredit(entry.CreditBytes);
        }
    }

    private readonly record struct CompletionEntry(
        long PipeOffset,
        int CreditBytes,
        Action<int> ReleaseCredit);
}
