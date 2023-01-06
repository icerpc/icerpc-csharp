// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests.Common;

[Flags]
public enum DuplexTransportOperation
{
    None = 0,
    Connect = 1,
    Read = 2,
    Shutdown = 4,
    Write = 8
}

/// <summary>A decorator for duplex server transport that holds any ConnectAsync and ShutdownAsync for connections
/// accepted by this transport.</summary>
#pragma warning disable CA1001 // _listener is disposed by Listen caller.
public class TestDuplexServerTransportDecorator : IDuplexServerTransport
#pragma warning restore CA1001
{
    public DuplexTransportOperation FailOperation
    {
        get => _listener?.FailOperation ?? throw new InvalidOperationException("Call Listen first.");

        set
        {
            if (_listener is null)
            {
                throw new InvalidOperationException("Call Listen first.");
            }
            _listener.FailOperation = value;
        }
    }

    public DuplexTransportOperation HoldOperation
    {
        get => _listener?.HoldOperation ?? throw new InvalidOperationException("Call Listen first.");

        set
        {
            if (_listener is null)
            {
                throw new InvalidOperationException("Call Listen first.");
            }
            _listener.HoldOperation = value;
        }
    }

    public string Name => _decoratee.Name;

    public TestDuplexConnectionDecorator LastConnection =>
        _listener?.LastConnection ?? throw new InvalidOperationException("Call Listen first.");

    private readonly IDuplexServerTransport _decoratee;
    private readonly DuplexTransportOperation _failOperation;
    private readonly DuplexTransportOperation _holdOperation;
    private TestDuplexListenerDecorator? _listener;

    public TestDuplexServerTransportDecorator(
        IDuplexServerTransport decoratee,
        DuplexTransportOperation holdOperation = DuplexTransportOperation.None,
        DuplexTransportOperation failOperation = DuplexTransportOperation.None)
    {
        _decoratee = decoratee;
        _holdOperation = holdOperation;
        _failOperation = failOperation;
    }

    public IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("Test server transport doesn't support multiple listeners.");
        }
        _listener = new TestDuplexListenerDecorator(
            _decoratee.Listen(serverAddress, options, serverAuthenticationOptions))
            {
                HoldOperation = _holdOperation,
                FailOperation = _failOperation
            };

        return _listener;
    }

    private class TestDuplexListenerDecorator : IListener<IDuplexConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        internal DuplexTransportOperation HoldOperation { get; set; }
        internal DuplexTransportOperation FailOperation { get; set; }

        internal TestDuplexConnectionDecorator LastConnection
        {
            get => _lastConnection ?? throw new InvalidOperationException("Call AcceptAsync first.");
            private set => _lastConnection = value;
        }

        private readonly IListener<IDuplexConnection> _decoratee;
        private TestDuplexConnectionDecorator? _lastConnection;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);
            LastConnection = new TestDuplexConnectionDecorator(connection)
                {
                    HoldOperation = HoldOperation,
                    FailOperation = FailOperation
                };
            return (LastConnection, remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal TestDuplexListenerDecorator(IListener<IDuplexConnection> decoratee) => _decoratee = decoratee;
    }
}

public sealed class TestDuplexConnectionDecorator : IDuplexConnection
{
    public ServerAddress ServerAddress => _decoratee.ServerAddress;

    public Task DisposeCalled => _disposeCalledTcs.Task;

    public DuplexTransportOperation FailOperation { get; set; }

    public DuplexTransportOperation HoldOperation
    {
        get => _holdOperation;
        set
        {
            _holdOperation = value;

            if (_holdOperation.HasFlag(DuplexTransportOperation.Connect))
            {
                _holdConnectTcs = new();
            }
            else
            {
                _holdConnectTcs.TrySetResult();
            }

            if (_holdOperation.HasFlag(DuplexTransportOperation.Shutdown))
            {
                _holdShutdownTcs = new();
            }
            else
            {
                _holdShutdownTcs.TrySetResult();
            }

            if (_holdOperation.HasFlag(DuplexTransportOperation.Read))
            {
                _holdReadTcs = new();
            }
            else
            {
                _holdReadTcs.TrySetResult();
            }

            if (_holdOperation.HasFlag(DuplexTransportOperation.Write))
            {
                _holdWriteTcs = new();
            }
            else
            {
                _holdWriteTcs.TrySetResult();
            }
        }
    }

    private readonly IDuplexConnection _decoratee;
    private readonly TaskCompletionSource _disposeCalledTcs = new();
    private TaskCompletionSource _holdConnectTcs = new();
    private DuplexTransportOperation _holdOperation;
    private TaskCompletionSource _holdReadTcs = new();
    private TaskCompletionSource _holdShutdownTcs = new();
    private TaskCompletionSource _holdWriteTcs = new();

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        if (FailOperation.HasFlag(DuplexTransportOperation.Connect))
        {
            throw new IceRpcException(IceRpcError.IceRpcError, "Test transport connect failure");
        }
        await _holdConnectTcs.Task.WaitAsync(cancellationToken);
        return await _decoratee.ConnectAsync(cancellationToken);
    }

    public void Dispose()
    {
        _disposeCalledTcs.TrySetResult();
        _decoratee.Dispose();

        _holdConnectTcs.TrySetResult();
        _holdReadTcs.TrySetResult();
        _holdShutdownTcs.TrySetResult();
        _holdWriteTcs.TrySetResult();
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        await CheckFailAndHoldAsync();

        int count = await _decoratee.ReadAsync(buffer, cancellationToken);

        // Check again fail/hold condition in case the configuration was changed while AcceptAsync was pending.
        await CheckFailAndHoldAsync();

        return count;

        async Task CheckFailAndHoldAsync()
        {
            if (FailOperation.HasFlag(DuplexTransportOperation.Read))
            {
                throw new IceRpcException(IceRpcError.IceRpcError, "Test transport read failure");
            }
            await _holdReadTcs.Task.WaitAsync(cancellationToken);
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (FailOperation.HasFlag(DuplexTransportOperation.Shutdown))
        {
            throw new IceRpcException(IceRpcError.IceRpcError, "Test transport shutdown failure");
        }
        await _holdShutdownTcs.Task.WaitAsync(cancellationToken);
        await _decoratee.ShutdownAsync(cancellationToken);
    }

    public async ValueTask WriteAsync(
        IReadOnlyList<ReadOnlyMemory<byte>> buffers,
        CancellationToken cancellationToken)
    {
        if (FailOperation.HasFlag(DuplexTransportOperation.Read))
        {
            throw new IceRpcException(IceRpcError.IceRpcError, "Test transport write failure");
        }
        await _holdWriteTcs.Task.WaitAsync(cancellationToken);

        await _decoratee.WriteAsync(buffers, cancellationToken);
    }

    internal TestDuplexConnectionDecorator(IDuplexConnection decoratee) => _decoratee = decoratee;
}
