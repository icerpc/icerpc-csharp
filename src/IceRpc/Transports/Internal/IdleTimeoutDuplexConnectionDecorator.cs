// Copyright (c) ZeroC, Inc.

namespace IceRpc.Transports.Internal;

/// <summary>Decorates <see cref="ReadAsync" /> to fail if no byte is received for over idle timeout and <see
/// cref="WriteAsync" /> to enable the keep alive timer. The keep alive ping is set every idle timeout / 2 period. Both
/// side of the connection are supposed to use the same idle timeout.</summary>
internal class IdleTimeoutDuplexConnectionDecorator : IDuplexConnection
{
    private readonly IDuplexConnection _decoratee;
    private readonly TimeSpan _idleTimeout;
    private readonly Timer? _keepAliveTimer;
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

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
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

    public Task ShutdownAsync(CancellationToken cancellationToken) => _decoratee.ShutdownAsync(cancellationToken);

    public async ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken)
    {
        await _decoratee.WriteAsync(buffers, cancellationToken).ConfigureAwait(false);

        // After each successful write, we schedule one ping (keep alive) at _idleTimeout / 2 in the future. Since each
        // ping is itself a write, if there is no application activity at all, we'll send successive pings at
        // _idleTimeout / 2 intervals.
        _keepAliveTimer?.Change(_idleTimeout / 2, Timeout.InfiniteTimeSpan);
    }

    internal IdleTimeoutDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        TimeSpan idleTimeout,
        Action? keepAliveAction)
    {
        _decoratee = decoratee;
        _idleTimeout = idleTimeout;
        if (keepAliveAction is not null)
        {
            _keepAliveTimer = new Timer(_ => keepAliveAction());
            _keepAliveTimer?.Change(_idleTimeout / 2, Timeout.InfiniteTimeSpan);
        }
    }
}
