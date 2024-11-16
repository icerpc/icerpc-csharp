// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>Decorates <see cref="ReadAsync" /> to fail if no byte is received for over readIdleTimeout. Also decorates
/// <see cref="WriteAsync" /> to send a heartbeat (writeIdleTimeout / 2) after a successful write. Both sides of the
/// connection are expected to use the same idle timeouts.</summary>
internal class IceDuplexConnectionDecorator : IDuplexConnection
{
    private readonly IDuplexConnection _decoratee;
    private readonly CancellationTokenSource _readCts = new();
    private readonly TimeSpan _readIdleTimeout;
    private readonly TimeSpan _writeIdleTimeout;
    private readonly Timer _writeTimer;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        _decoratee.ConnectAsync(cancellationToken);

    public void Dispose()
    {
        _decoratee.Dispose();
        _readCts.Dispose();

        // Using Dispose is fine, there's no need to wait for the sendHeartbeat to complete if it's running.
        _writeTimer.Dispose();
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        return _readIdleTimeout == Timeout.InfiniteTimeSpan ?
            _decoratee.ReadAsync(buffer, cancellationToken) :
            PerformReadAsync();

        async ValueTask<int> PerformReadAsync()
        {
            try
            {
                using CancellationTokenRegistration _ = cancellationToken.UnsafeRegister(
                    cts => ((CancellationTokenSource)cts!).Cancel(),
                    _readCts);
                _readCts.CancelAfter(_readIdleTimeout); // enable idle timeout before reading
                return await _decoratee.ReadAsync(buffer, _readCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                throw new IceRpcException(
                    IceRpcError.ConnectionIdle,
                    $"The connection did not receive any bytes for over {_readIdleTimeout.TotalSeconds} s.");
            }
            finally
            {
                _readCts.CancelAfter(Timeout.InfiniteTimeSpan); // disable idle timeout if not canceled
            }
        }
    }

    public Task ShutdownWriteAsync(CancellationToken cancellationToken) =>
        _decoratee.ShutdownWriteAsync(cancellationToken);

    public ValueTask WriteAsync(ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        return _writeIdleTimeout == Timeout.InfiniteTimeSpan ?
            _decoratee.WriteAsync(buffer, cancellationToken) :
            PerformWriteAsync();

        async ValueTask PerformWriteAsync()
        {
            // No need to send a heartbeat now since we're about to write.
            CancelWriteTimer();

            await _decoratee.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);

            // After each successful write, we schedule a heartbeat at _writeIdleTimeout / 2 in the future.
            // Since each heartbeat is itself a write, if there is no application activity at all, we'll send successive
            // heartbeats at _writeIdleTimeout / 2 intervals.
            RescheduleWriteTimer();
        }
    }

    /// <summary>Constructs a decorator that ensures a call to <see cref="ReadAsync" /> will fail after readIdleTimeout.
    /// This decorator also schedules a heartbeat after each write (see <see cref="RescheduleWriteTimer" />).</summary>
    internal IceDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        TimeSpan readIdleTimeout,
        TimeSpan writeIdleTimeout,
        Action sendHeartbeat)
    {
        Debug.Assert(writeIdleTimeout != Timeout.InfiniteTimeSpan);
        _decoratee = decoratee;
        _readIdleTimeout = readIdleTimeout; // can be infinite i.e. disabled
        _writeIdleTimeout = writeIdleTimeout;
        _writeTimer = new Timer(_ => sendHeartbeat());
        // We can't schedule the initial heartbeat yet. The heartbeat is an ice protocol frame; we can send it only once
        // the connection is connected at the ice protocol level.
    }

    /// <summary>Schedules the initial heartbeat. Called by a client IceProtocolConnection once it receives the
    /// initial ValidateConnection frame from the server.</summary>
    internal void ScheduleHeartbeat() => RescheduleWriteTimer();

    /// <summary>Cancels the write timer.</summary>
    private void CancelWriteTimer() => _writeTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    /// <summary>Schedules or reschedules the write timer. We send a heartbeat when this timer expires.</summary>
    private void RescheduleWriteTimer() => _writeTimer.Change(_writeIdleTimeout / 2, Timeout.InfiniteTimeSpan);
}
