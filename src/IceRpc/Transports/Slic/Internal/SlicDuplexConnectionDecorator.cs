// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Transports.Slic.Internal;

/// <summary>Decorates <see cref="ReadAsync" /> to fail if no byte is received for over idle timeout. Also optionally
/// decorates both <see cref="ReadAsync"/> and <see cref="WriteAsync" /> to schedule pings that prevent both the local
/// and remote idle timers from expiring.</summary>
internal class SlicDuplexConnectionDecorator : IDuplexConnection
{
    private readonly IDuplexConnection _decoratee;
    private TimeSpan _idleTimeout = Timeout.InfiniteTimeSpan;
    private readonly CancellationTokenSource _readCts = new();

    private readonly Timer? _readTimer;
    private readonly Timer? _writeTimer;

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        _decoratee.ConnectAsync(cancellationToken);

    public void Dispose()
    {
        _decoratee.Dispose();
        _readCts.Dispose();

        // Using Dispose is fine, there's no need to wait for the keep alive action to terminate if it's running.
        _readTimer?.Dispose();
        _writeTimer?.Dispose();
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

                int bytesRead = await _decoratee.ReadAsync(buffer, _readCts.Token).ConfigureAwait(false);

                // After each successful read, we schedule one ping some time in the future.
                if (bytesRead > 0)
                {
                    ResetReadTimer();
                }
                // When 0, the other side called ShutdownWriteAsync, so there is no point to send a ping since we can't
                // get back a pong.

                return bytesRead;
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

    public Task ShutdownWriteAsync(CancellationToken cancellationToken) =>
        _decoratee.ShutdownWriteAsync(cancellationToken);

    public ValueTask WriteAsync(ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        return _idleTimeout == Timeout.InfiniteTimeSpan ?
            _decoratee.WriteAsync(buffer, cancellationToken) :
            PerformWriteAsync();

        async ValueTask PerformWriteAsync()
        {
            await _decoratee.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);

            // After each successful write, we schedule one ping some time in the future. Since each ping is itself a
            // write, if there is no application activity at all, we'll send successive pings at regular intervals.
            ResetWriteTimer();
        }
    }

    /// <summary>Constructs a decorator that does nothing until it is enabled by a call to <see cref="Enable"/>.
    /// </summary>
    internal SlicDuplexConnectionDecorator(IDuplexConnection decoratee) => _decoratee = decoratee;

    /// <summary>Constructs a decorator that does nothing until it is enabled by a call to <see cref="Enable"/>.
    /// </summary>
    internal SlicDuplexConnectionDecorator(IDuplexConnection decoratee, Action sendReadPing, Action sendWritePing)
        : this(decoratee)
    {
        _readTimer = new Timer(_ => sendReadPing());
        _writeTimer = new Timer(_ => sendWritePing());
    }

    /// <summary>Sets the idle timeout and schedules pings once the connection is established.</summary>.
    internal void Enable(TimeSpan idleTimeout)
    {
        Debug.Assert(idleTimeout != Timeout.InfiniteTimeSpan);
        _idleTimeout = idleTimeout;

        ResetReadTimer();
        ResetWriteTimer();
    }

    /// <summary>Resets the read timer. We send a "read" ping when this timer expires.</summary>
    /// <remarks>This method is no-op unless this decorator is constructed with send ping actions.</remarks>
    private void ResetReadTimer() => _readTimer?.Change(_idleTimeout * 0.5, Timeout.InfiniteTimeSpan);

    /// <summary>Resets the write timer. We send a "write" ping when this timer expires.</summary>
    /// <remarks>This method is no-op unless this decorator is constructed with send ping actions.</remarks>
    // The write timer factor (0.6) was chosen to be greater than the read timer factor (0.5). This way, when the
    // connection is completely idle, the read timer expires before the write timer and has time to send a ping that
    // resets the write timer. This reduces the likelihood of duplicate "keep alive" pings.
    private void ResetWriteTimer() => _writeTimer?.Change(_idleTimeout * 0.6, Timeout.InfiniteTimeSpan);
}
