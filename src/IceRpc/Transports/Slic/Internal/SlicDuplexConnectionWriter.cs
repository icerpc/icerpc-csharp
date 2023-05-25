// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>A helper class to write data to a duplex connection. Its methods shouldn't be called concurrently. The data
/// written to this writer is copied and buffered with an internal pipe. The data from the pipe is written on the duplex
/// connection with a background task.</summary>
internal class SlicDuplexConnectionWriter : IBufferWriter<byte>, IAsyncDisposable
{
    private readonly Task _backgroundWriteTask;
    private readonly IDuplexConnection _connection;
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _disposeTask;
    private int _pendingWriterCount;
    private readonly long _flushThreshold;
    private readonly Pipe _pipe;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

    public void Advance(int count) => _pipe.Writer.Advance(count);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _disposeTask ??= PerformDisposeAsync();
        return new(_disposeTask);

        async Task PerformDisposeAsync()
        {
            _disposeCts.Cancel();

            await Task.WhenAll(_backgroundWriteTask, _writeSemaphore.WaitAsync()).ConfigureAwait(false);

            _pipe.Reader.Complete();
            _pipe.Writer.Complete();

            _writeSemaphore.Dispose();
            _disposeCts.Dispose();
        }
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint) => _pipe.Writer.GetMemory(sizeHint);

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint) => _pipe.Writer.GetSpan(sizeHint);

    /// <summary>Constructs a duplex connection writer.</summary>
    /// <param name="connection">The duplex connection to write to.</param>
    /// <param name="flushThreshold">The flush threshold.</param>
    /// <param name="pool">The memory pool to use.</param>
    /// <param name="minimumSegmentSize">The minimum segment size for buffers allocated from <paramref
    /// name="pool"/>.</param>
    internal SlicDuplexConnectionWriter(
        IDuplexConnection connection,
        int flushThreshold,
        MemoryPool<byte> pool,
        int minimumSegmentSize)
    {
        _connection = connection;
        _flushThreshold = flushThreshold;

        // We set pauseWriterThreshold to 0 because Slic implements flow-control at the stream level. So there's no need
        // to limit the amount of data buffered by the writer pipe. The amount of data buffered is limited to
        // (MaxBidirectionalStreams + MaxUnidirectionalStreams) * PeerPauseWriterThreshold bytes.
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            resumeWriterThreshold: 0));

        _backgroundWriteTask = Task.Run(
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

    internal async ValueTask<SlicDuplexConnectionWriterLock> AcquireAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _pendingWriterCount);
        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SlicDuplexConnectionWriterLock(this);
    }

    /// <summary>Requests the shut down of the duplex connection after the buffered data is written on the duplex
    /// connection.</summary>
    internal void Shutdown() => _pipe.Writer.Complete();

    internal void Release()
    {
        if (Interlocked.Decrement(ref _pendingWriterCount) == 0 || _pipe.Writer.UnflushedBytes > _flushThreshold)
        {
            ValueTask<FlushResult> flushResult = _pipe.Writer.FlushAsync(CancellationToken.None);

            // PauseWriterThreshold is 0 so FlushAsync should always complete synchronously.
            Debug.Assert(flushResult.IsCompleted);
        }
        _writeSemaphore.Release();
    }
}

internal readonly struct SlicDuplexConnectionWriterLock : IDisposable
{
    private readonly SlicDuplexConnectionWriter _duplexConnectionWriter;

    public void Dispose() => _duplexConnectionWriter.Release();

    internal SlicDuplexConnectionWriterLock(SlicDuplexConnectionWriter duplexConnectionWriter) =>
        _duplexConnectionWriter = duplexConnectionWriter;
}
