// Copyright (c) ZeroC, Inc.

using System.Diagnostics;

namespace IceRpc.Transports.Internal;

/// <summary>Decorates <see cref="ReadAsync" /> to fail if no byte is received for over idle timeout and <see
/// cref="WriteAsync" /> to schedule the keep alive timer after (idleTimeout / 2) on a successfull write. Both sides of
/// the connection are expected to use the same idle timeout.</summary>
internal class IdleTimeoutDuplexConnectionDecorator : IDuplexConnection
{
    private readonly IDuplexConnection _decoratee;
    private TimeSpan _idleTimeout = Timeout.InfiniteTimeSpan;
    private Timer? _keepAliveTimer;
    private readonly CancellationTokenSource _readCts = new();

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
        return _idleTimeout == Timeout.InfiniteTimeSpan ?
            _decoratee.ReadAsync(buffer, cancellationToken) :
            PerformReadAsync();

        async ValueTask<int> PerformReadAsync()
        {
            try
            {
                using CancellationTokenRegistration _ = cancellationToken.UnsafeRegister(
                    cts => ((CancellationTokenSource)cts!).Cancel(),
                    _readCts);
                _readCts.CancelAfter(_idleTimeout); // enable idle timeout before reading
                return await _decoratee.ReadAsync(buffer, _readCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();

                throw new IceRpcException(
                    IceRpcError.ConnectionIdle,
                    $"The connection did not receive any bytes for over {_idleTimeout.TotalSeconds} s.");
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
        return _idleTimeout == Timeout.InfiniteTimeSpan ?
            _decoratee.WriteAsync(buffers, cancellationToken) :
            PerformWriteAsync();

        async ValueTask PerformWriteAsync()
        {
            await _decoratee.WriteAsync(buffers, cancellationToken).ConfigureAwait(false);

            // After each successful write, we schedule one ping (keep alive) at _idleTimeout / 2 in the future. Since
            // each ping is itself a write, if there is no application activity at all, we'll send successive pings at
            // _idleTimeout / 2 intervals.
            ScheduleKeepAlive();
        }
    }

    /// <summary>Constructs a decorator that does nothing until it is enabled by a call to <see cref="Enable" />.
    /// </summary>
    internal IdleTimeoutDuplexConnectionDecorator(IDuplexConnection decoratee) => _decoratee = decoratee;

    /// <summary>Constructs a decorator that ensures a call to <see cref="ReadAsync" /> will fail after idleTimeout.
    /// This decorator also schedules a keepAliveAction after each write (see <see cref="ScheduleKeepAlive" />).
    /// </summary>
    /// <remarks>Do not call <see cref="Enable" /> on a decorator constructed with this constructor.</remarks>
    internal IdleTimeoutDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        TimeSpan idleTimeout,
        Action keepAliveAction)
        : this(decoratee)
    {
        Debug.Assert(idleTimeout != Timeout.InfiniteTimeSpan);
        _idleTimeout = idleTimeout;
        _keepAliveTimer = new Timer(_ => keepAliveAction());
    }

    /// <summary>Enables the read idle timeout and the scheduling of one keep alive after each write; also schedules one
    /// keep-alive.</summary>.
    internal void Enable(TimeSpan idleTimeout, Action? keepAliveAction)
    {
        Debug.Assert(idleTimeout != Timeout.InfiniteTimeSpan);
        Debug.Assert(_keepAliveTimer is null);

        _idleTimeout = idleTimeout;

        if (keepAliveAction is not null)
        {
            _keepAliveTimer = new Timer(_ => keepAliveAction());
            ScheduleKeepAlive();
        }
    }

    /// <summary>Schedules one keep alive in idleTimeout / 2.</summary>
    internal void ScheduleKeepAlive() => _keepAliveTimer?.Change(_idleTimeout / 2, Timeout.InfiniteTimeSpan);
}
