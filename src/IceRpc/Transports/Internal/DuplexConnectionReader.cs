// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal;

/// <summary>A helper class to efficiently read data from a duplex connection. It provides a PipeReader-like API but is
/// not a PipeReader.</summary>
internal class DuplexConnectionReader : IDisposable
{
    private readonly IDuplexConnection _connection;
    private TimeSpan _idleTimeout;
    private readonly Timer _idleTimeoutTimer;
    private bool _isDisposed;
    private readonly Timer? _keepAliveTimer;
    private readonly object _mutex = new();
    private TimeSpan _nextIdleTime;
    private readonly Pipe _pipe;

    public void Dispose()
    {
        lock (_mutex)
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
        }

        _pipe.Writer.Complete();
        _pipe.Reader.Complete();
        _idleTimeoutTimer.Dispose();
        _keepAliveTimer?.Dispose();
    }

    internal DuplexConnectionReader(
        IDuplexConnection connection,
        TimeSpan idleTimeout,
        MemoryPool<byte> pool,
        int minimumSegmentSize,
        Action<ConnectionLostException> connectionLostAction,
        Action? keepAliveAction)
    {
        _connection = connection;
        _pipe = new Pipe(new PipeOptions(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            writerScheduler: PipeScheduler.Inline));
        _idleTimeout = idleTimeout;
        _nextIdleTime = TimeSpan.Zero;

        // Setup a timer to abort the connection if it's idle for longer than the idle timeout.
        _idleTimeoutTimer = new Timer(
            _ =>
            {
                lock (_mutex)
                {
                    if (_nextIdleTime >= TimeSpan.FromMilliseconds(Environment.TickCount64))
                    {
                        // The idle timeout has just been postponed. Don't abort the connection since this indicates
                        // data was just received.
                        return;
                    }

                    // Set the next idle time to the infinite timeout to ensure ResetTimers fails. Timers can't be
                    // reset once the connection abort is initiated.
                    _nextIdleTime = Timeout.InfiniteTimeSpan;
                }

                connectionLostAction(new ConnectionLostException(
                    $"the transport connection has been idle for longer than {_idleTimeout}"));
            });

        if (keepAliveAction is not null)
        {
            _keepAliveTimer = new Timer(
                _ =>
                {
                    lock (_mutex)
                    {
                        if (_isDisposed)
                        {
                            return;
                        }

                        // Postpone the idle timeout.
                        _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                    }

                    // Keep the connection alive.
                    keepAliveAction.Invoke();
                });
        }
    }

    internal void AdvanceTo(SequencePosition consumed) => _pipe.Reader.AdvanceTo(consumed);

    internal void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
        _pipe.Reader.AdvanceTo(consumed, examined);

    /// <summary>Writes <paramref name="byteCount"/> bytes read from this pipe reader or its underlying connection
    /// into <paramref name="bufferWriter"/>.</summary>
    internal ValueTask FillBufferWriterAsync(
        IBufferWriter<byte> bufferWriter,
        int byteCount,
        CancellationToken cancellationToken)
    {
        if (byteCount == 0)
        {
            return default;
        }

        // If there's still data on the pipe reader, copy the data from the pipe reader synchronously.
        if (_pipe.Reader.TryRead(out ReadResult readResult))
        {
            Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);

            ReadOnlySequence<byte> buffer = readResult.Buffer;
            if (buffer.Length > byteCount)
            {
                buffer = buffer.Slice(0, byteCount);
            }

            bufferWriter.Write(buffer);
            _pipe.Reader.AdvanceTo(buffer.End);

            byteCount -= (int)buffer.Length;

            if (byteCount == 0)
            {
                return default;
            }
        }

        return ReadFromConnectionAsync(byteCount);

        // Read the remaining bytes directly from the connection into the buffer writer.
        async ValueTask ReadFromConnectionAsync(int byteCount)
        {
            do
            {
                Memory<byte> buffer = bufferWriter.GetMemory();
                if (buffer.Length > byteCount)
                {
                    buffer = buffer[0..byteCount];
                }

                int read = await _connection.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                bufferWriter.Advance(read);
                byteCount -= read;

                ResetTimers();

                if (byteCount > 0 && read == 0)
                {
                    // The peer gracefully shut down the connection but returned less data than expected, it's
                    // considered as an error.
                    throw new InvalidDataException("received less data than expected");
                }
            }
            while (byteCount > 0);
        }
    }

    /// <summary>Reads and returns bytes from the underlying transport connection. The returned buffer can be empty if
    /// the peer shutdown its side of the connection.</summary>
    internal ValueTask<ReadOnlySequence<byte>> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadAsyncCore(minimumSize: 1, canReturnEmptyBuffer: true, cancellationToken);

    /// <summary>Reads and returns bytes from the underlying transport connection. The returned buffer has always
    /// at least minimumSize bytes.</summary>
    internal ValueTask<ReadOnlySequence<byte>> ReadAtLeastAsync(int minimumSize, CancellationToken cancellationToken = default) =>
        ReadAsyncCore(minimumSize: minimumSize, canReturnEmptyBuffer: false, cancellationToken);

    internal void EnableIdleCheck(TimeSpan? idleTimeout = null)
    {
        lock (_mutex)
        {
            if (_isDisposed)
            {
                return;
            }

            if (idleTimeout is not null)
            {
                _idleTimeout = idleTimeout.Value;
            }

            if (_idleTimeout == Timeout.InfiniteTimeSpan)
            {
                _idleTimeoutTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _keepAliveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _nextIdleTime = TimeSpan.Zero;
            }
            else
            {
                _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                _keepAliveTimer?.Change(_idleTimeout / 2, Timeout.InfiniteTimeSpan);
                _nextIdleTime = TimeSpan.FromMilliseconds(Environment.TickCount64) + _idleTimeout;
            }
        }
    }

    internal bool TryRead(out ReadOnlySequence<byte> buffer)
    {
        if (_pipe.Reader.TryRead(out ReadResult readResult))
        {
            Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);
            buffer = readResult.Buffer;
            return true;
        }
        else
        {
            buffer = default;
            return false;
        }
    }

    private void ResetTimers()
    {
        lock (_mutex)
        {
            Debug.Assert(!_isDisposed);

            if (_idleTimeout == Timeout.InfiniteTimeSpan)
            {
                // Nothing to do, idle timeout is disabled.
            }
            else if (_nextIdleTime == Timeout.InfiniteTimeSpan)
            {
                // The idle timeout timer aborted the connection. Don't reset the timers and throw to ensure the
                // calling read method doesn't return data.
                throw new ConnectionLostException(
                    $"the transport connection has been idle for longer than {_idleTimeout}");
            }
            else
            {
                // Postpone the idle timeout and keep the connection alive.
                _idleTimeoutTimer.Change(_idleTimeout, Timeout.InfiniteTimeSpan);
                _keepAliveTimer?.Change(_idleTimeout / 2, Timeout.InfiniteTimeSpan);
                _nextIdleTime = TimeSpan.FromMilliseconds(Environment.TickCount64) + _idleTimeout;
            }
        }
    }

    /// <summary>Reads and returns bytes from the underlying transport connection. The returned buffer has always at
    /// least minimumSize bytes or if canReturnEmptyBuffer is true, the returned buffer can be empty if the peer
    /// shutdown the connection.</summary>
    private async ValueTask<ReadOnlySequence<byte>> ReadAsyncCore(
        int minimumSize,
        bool canReturnEmptyBuffer,
        CancellationToken cancellationToken = default)
    {
        Debug.Assert(minimumSize > 0);

        // Read buffered data first.
        if (_pipe.Reader.TryRead(out ReadResult readResult))
        {
            Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled && !readResult.Buffer.IsEmpty);
            if (readResult.Buffer.Length >= minimumSize)
            {
                return readResult.Buffer;
            }
            _pipe.Reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            minimumSize -= (int)readResult.Buffer.Length;
        }

        do
        {
            // Fill the pipe with data read from the connection.
            Memory<byte> buffer = _pipe.Writer.GetMemory();
            int read = await _connection.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _pipe.Writer.Advance(read);
            minimumSize -= read;

            ResetTimers();

            // The peer shutdown its side of the connection, return an empty buffer if allowed.
            if (read == 0)
            {
                if (canReturnEmptyBuffer)
                {
                    break;
                }
                else
                {
                    // The peer gracefully shut down the connection but returned less data than expected, it's
                    // considered as an error.
                    throw new InvalidDataException("received less data than expected");
                }
            }
        }
        while (minimumSize > 0);

        _ = await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        _pipe.Reader.TryRead(out readResult);
        Debug.Assert(!readResult.IsCompleted && !readResult.IsCanceled);

        return readResult.Buffer;
    }
}
