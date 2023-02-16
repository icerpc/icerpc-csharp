// Copyright (c) ZeroC, Inc.

namespace IceRpc.Transports.Internal;

/// <summary>Decorates <see cref="ReadAsync" /> to fail if no byte is received for over idle timeout and <see
/// cref="WriteAsync" /> to enable the keep alive timer.</summary>
internal class IdleTimeoutDuplexConnectionDecorator : IDuplexConnection
{
    private readonly IDuplexConnection _decoratee;
    private readonly TimeSpan _idleTimeout;
    private readonly CancellationTokenSource _readCts = new();

    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        _decoratee.ConnectAsync(cancellationToken);

    public void Dispose()
    {
        _decoratee.Dispose();
        _readCts.Dispose();
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

    public ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken) =>
        _decoratee.WriteAsync(buffers, cancellationToken);

    internal IdleTimeoutDuplexConnectionDecorator(IDuplexConnection decoratee, TimeSpan idleTimeout)
    {
        _decoratee = decoratee;
        _idleTimeout = idleTimeout;
    }
}
