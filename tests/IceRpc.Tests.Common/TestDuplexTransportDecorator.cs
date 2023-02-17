// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests.Common;

#pragma warning disable CS1591 // TODO fix with #2704

[Flags]
public enum DuplexTransportOperation
{
    None = 0,
    Connect = 1,
    Read = 2,
    Shutdown = 4,
    Write = 8
}

#pragma warning disable CA1001 // _lastConnection is disposed by the caller.
public sealed class TestDuplexClientTransportDecorator : IDuplexClientTransport
#pragma warning restore CA1001
{
    public TestDuplexConnectionDecorator LastConnection =>
        _lastConnection ?? throw new InvalidOperationException("Call CreateConnection first.");

    private readonly IDuplexClientTransport _decoratee;
    private readonly DuplexTransportOperation _failOperation;
    private readonly Exception _failureException;
    private readonly DuplexTransportOperation _holdOperation;
    private TestDuplexConnectionDecorator? _lastConnection;

    public string Name => _decoratee.Name;

    public bool CheckParams(ServerAddress serverAddress) => _decoratee.CheckParams(serverAddress);

    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        var connection = new TestDuplexConnectionDecorator(_decoratee.CreateConnection(
            serverAddress,
            options,
            clientAuthenticationOptions))
        {
            HoldOperation = _holdOperation,
            FailOperation = _failOperation,
            FailureException = _failureException
        };
        _lastConnection = connection;
        return connection;
    }

    public TestDuplexClientTransportDecorator(
        IDuplexClientTransport decoratee,
        DuplexTransportOperation holdOperation = DuplexTransportOperation.None,
        DuplexTransportOperation failOperation = DuplexTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        _holdOperation = holdOperation;
        _failOperation = failOperation;
        _failureException = failureException ?? new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");
    }
}

/// <summary>A decorator for duplex server transport that holds any ConnectAsync and ShutdownAsync for connections
/// accepted by this transport.</summary>
#pragma warning disable CA1001 // _listener is disposed by Listen caller.
public class TestDuplexServerTransportDecorator : IDuplexServerTransport
#pragma warning restore CA1001
{
    public DuplexTransportOperation FailOperation
    {
        get => _listener?.FailOperation ?? _failOperation;

        set
        {
            if (_listener is null)
            {
                _failOperation = value;
            }
            else
            {
                _listener.FailOperation = value;
            }
        }
    }

    public Exception FailureException
    {
        get => _listener?.FailureException ?? _failureException;

        set
        {
            if (_listener is null)
            {
                _failureException = value;
            }
            else
            {
                _listener.FailureException = value;
            }
        }
    }

    public DuplexTransportOperation HoldOperation
    {
        get => _listener?.HoldOperation ?? _holdOperation;

        set
        {
            if (_listener is null)
            {
                _holdOperation = value;
            }
            else
            {
                _listener.HoldOperation = value;
            }
        }
    }

    public string Name => _decoratee.Name;

    public TestDuplexConnectionDecorator LastAcceptedConnection =>
        _listener?.LastAcceptedConnection ?? throw new InvalidOperationException("Call Listen first.");

    private readonly IDuplexServerTransport _decoratee;
    private DuplexTransportOperation _failOperation;
    private Exception _failureException;
    private DuplexTransportOperation _holdOperation;
    private TestDuplexListenerDecorator? _listener;

    public TestDuplexServerTransportDecorator(
        IDuplexServerTransport decoratee,
        DuplexTransportOperation holdOperation = DuplexTransportOperation.None,
        DuplexTransportOperation failOperation = DuplexTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        _holdOperation = holdOperation;
        _failOperation = failOperation;
        _failureException = failureException ?? new IceRpcException(IceRpcError.IceRpcError, "Test transport failure");
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
                FailOperation = _failOperation,
                FailureException = _failureException
            };

        return _listener;
    }

    private class TestDuplexListenerDecorator : IListener<IDuplexConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        internal DuplexTransportOperation HoldOperation { get; set; }
        internal DuplexTransportOperation FailOperation { get; set; }
        internal Exception FailureException { get; set; } = new Exception();

        internal TestDuplexConnectionDecorator LastAcceptedConnection
        {
            get => _lastAcceptedConnection ?? throw new InvalidOperationException("Call AcceptAsync first.");
            private set => _lastAcceptedConnection = value;
        }

        private readonly IListener<IDuplexConnection> _decoratee;
        private TestDuplexConnectionDecorator? _lastAcceptedConnection;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);
            LastAcceptedConnection = new TestDuplexConnectionDecorator(connection)
                {
                    HoldOperation = HoldOperation,
                    FailOperation = FailOperation,
                    FailureException = FailureException
                };
            return (LastAcceptedConnection, remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal TestDuplexListenerDecorator(IListener<IDuplexConnection> decoratee) => _decoratee = decoratee;
    }
}

public sealed class TestDuplexConnectionDecorator : IDuplexConnection
{
    public Task DisposeCalled => _disposeCalledTcs.Task;

    public DuplexTransportOperation FailOperation { get; set; }

    public Exception FailureException { get; set; } = new Exception();

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
    private readonly TaskCompletionSource _disposeCalledTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _holdConnectTcs = new();
    private DuplexTransportOperation _holdOperation;
    private TaskCompletionSource _holdReadTcs = new();
    private TaskCompletionSource _holdShutdownTcs = new();
    private TaskCompletionSource _holdWriteTcs = new();

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        if (FailOperation.HasFlag(DuplexTransportOperation.Connect))
        {
            throw FailureException;
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
                throw FailureException;
            }
            await _holdReadTcs.Task.WaitAsync(cancellationToken);
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (FailOperation.HasFlag(DuplexTransportOperation.Shutdown))
        {
            throw FailureException;
        }
        await _holdShutdownTcs.Task.WaitAsync(cancellationToken);
        await _decoratee.ShutdownAsync(cancellationToken);
    }

    public async ValueTask WriteAsync(
        IReadOnlyList<ReadOnlyMemory<byte>> buffers,
        CancellationToken cancellationToken)
    {
        if (FailOperation.HasFlag(DuplexTransportOperation.Write))
        {
            throw FailureException;
        }
        await _holdWriteTcs.Task.WaitAsync(cancellationToken);

        await _decoratee.WriteAsync(buffers, cancellationToken);
    }

    internal TestDuplexConnectionDecorator(IDuplexConnection decoratee) => _decoratee = decoratee;
}

public static class TestDuplexTransportServiceCollectionExtensions
{
    /// <summary>Installs the test duplex transport.</summary>
    public static IServiceCollection AddTestDuplexTransport(
        this IServiceCollection services,
        DuplexTransportOperation clientHoldOperation = DuplexTransportOperation.None,
        DuplexTransportOperation clientFailOperation = DuplexTransportOperation.None,
        Exception? clientFailureException = null,
        DuplexTransportOperation serverHoldOperation = DuplexTransportOperation.None,
        DuplexTransportOperation serverFailOperation = DuplexTransportOperation.None,
        Exception? serverFailureException = null) => services
        .AddSingleton<ColocTransportOptions>()
        .AddSingleton(provider => new ColocTransport(provider.GetRequiredService<ColocTransportOptions>()))
        .AddSingleton(provider =>
            new TestDuplexClientTransportDecorator(
                provider.GetRequiredService<ColocTransport>().ClientTransport,
                holdOperation: clientHoldOperation,
                failOperation: clientFailOperation,
                failureException: clientFailureException))
        .AddSingleton<IDuplexClientTransport>(provider =>
            provider.GetRequiredService<TestDuplexClientTransportDecorator>())
        .AddSingleton(provider =>
            new TestDuplexServerTransportDecorator(
                provider.GetRequiredService<ColocTransport>().ServerTransport,
                holdOperation: serverHoldOperation,
                failOperation: serverFailOperation,
                failureException: serverFailureException))
        .AddSingleton<IDuplexServerTransport>(provider =>
            provider.GetRequiredService<TestDuplexServerTransportDecorator>());
}
