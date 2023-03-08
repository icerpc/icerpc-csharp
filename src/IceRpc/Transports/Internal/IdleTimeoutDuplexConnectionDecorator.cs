// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace IceRpc.Transports.Internal;

/// <summary>Decorates <see cref="ReadAsync" /> to fail if no byte is received for over (read) idle timeout. Also
/// decorates <see cref="WriteAsync" /> to schedule a keep alive action (writeIdleTimeout / 2) after a successful
/// write. Both sides of the connection are expected to use the same idle timeouts.</summary>
internal class IdleTimeoutDuplexConnectionDecorator : IDuplexConnection
{
    private readonly IDuplexConnection _decoratee;
    private Timer? _keepAliveTimer;
    private readonly CancellationTokenSource _readCts = new();
    private TimeSpan _readIdleTimeout = Timeout.InfiniteTimeSpan;
    private TimeSpan _writeIdleTimeout = Timeout.InfiniteTimeSpan;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        _decoratee.ConnectAsync(cancellationToken);

    public void Dispose()
    {
        _decoratee.Dispose();
        _readCts.Dispose();

        // Using Dispose is fine, there's no need to wait for the keep alive action to terminate if it's running.
        _keepAliveTimer?.Dispose();
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

    public Task ShutdownAsync(CancellationToken cancellationToken) => _decoratee.ShutdownAsync(cancellationToken);

    public ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken)
    {
        return _writeIdleTimeout == Timeout.InfiniteTimeSpan ?
            _decoratee.WriteAsync(buffers, cancellationToken) :
            PerformWriteAsync();

        async ValueTask PerformWriteAsync()
        {
            await _decoratee.WriteAsync(buffers, cancellationToken).ConfigureAwait(false);

            // After each successful write, we schedule one ping (keep alive or heartbeat) at _writeIdleTimeout / 2 in
            // the future.  Since each ping is itself a write, if there is no application activity at all, we'll send
            // successive pings at _writeIdleTimeout / 2 intervals.
            ScheduleKeepAlive();
        }
    }

    /// <summary>Constructs a decorator that does nothing until it is enabled by a call to <see cref="Enable" />.
    /// </summary>
    internal IdleTimeoutDuplexConnectionDecorator(IDuplexConnection decoratee) => _decoratee = decoratee;

    /// <summary>Constructs a decorator that ensures a call to <see cref="ReadAsync" /> will fail after readIdleTimeout.
    /// This decorator also schedules a keepAliveAction after each write (see <see cref="ScheduleKeepAlive" />).
    /// </summary>
    /// <remarks>Do not call <see cref="Enable" /> on a decorator constructed with this constructor.</remarks>
    internal IdleTimeoutDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        TimeSpan readIdleTimeout,
        TimeSpan writeIdleTimeout,
        Action keepAliveAction)
        : this(decoratee)
    {
        Debug.Assert(writeIdleTimeout != Timeout.InfiniteTimeSpan);
        _readIdleTimeout = readIdleTimeout; // can be infinite i.e. disabled
        _writeIdleTimeout = writeIdleTimeout;
        _keepAliveTimer = new Timer(_ => keepAliveAction());
    }

    /// <summary>Enables the read idle timeout and the scheduling of one keep alive after each write; also schedules one
    /// keep-alive.</summary>.
    internal void Enable(TimeSpan idleTimeout, Action? keepAliveAction)
    {
        Debug.Assert(idleTimeout != Timeout.InfiniteTimeSpan);
        Debug.Assert(_keepAliveTimer is null);

        _readIdleTimeout = idleTimeout;
        _writeIdleTimeout = idleTimeout;

        if (keepAliveAction is not null)
        {
            _keepAliveTimer = new Timer(_ => keepAliveAction());
            ScheduleKeepAlive();
        }
    }

    /// <summary>Schedules one keep alive in writeIdleTimeout / 2.</summary>
    internal void ScheduleKeepAlive() => _keepAliveTimer?.Change(_writeIdleTimeout / 2, Timeout.InfiniteTimeSpan);
}
