// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests.Common;

[Flags]
public enum DuplexTransportOperation
{
    None = 0,
    Accept = 1,
    Connect = 2,
    Dispose = 4,
    Read = 8,
    Shutdown = 16,
    Write = 32,
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
        var connection = new TestDuplexConnectionDecorator(
            _decoratee.CreateConnection(serverAddress, options, clientAuthenticationOptions),
            _holdOperation,
            _failOperation,
            _failureException);
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
    public TestTransportOperationHelper<DuplexTransportOperation> Operations;

    public string Name => _decoratee.Name;

    public TestDuplexConnectionDecorator LastAcceptedConnection =>
        _listener?.LastAcceptedConnection ?? throw new InvalidOperationException("Call Listen first.");

    private readonly IDuplexServerTransport _decoratee;
    private TestDuplexListenerDecorator? _listener;

    public TestDuplexServerTransportDecorator(
        IDuplexServerTransport decoratee,
        DuplexTransportOperation holdOperation = DuplexTransportOperation.None,
        DuplexTransportOperation failOperation = DuplexTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        Operations = new(holdOperation, failOperation, failureException);
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
            _decoratee.Listen(serverAddress, options, serverAuthenticationOptions),
            Operations);

        return _listener;
    }

    private class TestDuplexListenerDecorator : IListener<IDuplexConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        internal TestDuplexConnectionDecorator LastAcceptedConnection
        {
            get => _lastAcceptedConnection ?? throw new InvalidOperationException("Call AcceptAsync first.");
            private set => _lastAcceptedConnection = value;
        }

        private readonly IListener<IDuplexConnection> _decoratee;
        private TestDuplexConnectionDecorator? _lastAcceptedConnection;
        private readonly TestTransportOperationHelper<DuplexTransportOperation> _operations;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            await _operations.CheckAsync(DuplexTransportOperation.Accept, cancellationToken);

            (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await _operations.CheckAsync(DuplexTransportOperation.Accept, cancellationToken);
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            LastAcceptedConnection = new TestDuplexConnectionDecorator(
                    connection,
                    _operations.Hold,
                    _operations.Fail,
                    _operations.FailureException);
            return (LastAcceptedConnection, remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal TestDuplexListenerDecorator(
            IListener<IDuplexConnection> decoratee,
            TestTransportOperationHelper<DuplexTransportOperation> operations)
        {
            _decoratee = decoratee;
            _operations = operations;
        }
    }
}

public sealed class TestDuplexConnectionDecorator : IDuplexConnection
{
    public TestTransportOperationHelper<DuplexTransportOperation> Operations { get; }

    private readonly IDuplexConnection _decoratee;

    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperation.Connect, cancellationToken);
        return await _decoratee.ConnectAsync(cancellationToken);
    }

    public void Dispose()
    {
        Operations.Called(DuplexTransportOperation.Dispose);
        _decoratee.Dispose();
        Operations.Complete();
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperation.Read, cancellationToken);

        int count = await _decoratee.ReadAsync(buffer, cancellationToken);

        // Check again fail/hold condition in case the configuration was changed while AcceptAsync was pending.
        await Operations.CheckAsync(DuplexTransportOperation.Read, cancellationToken);

        return count;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperation.Shutdown, cancellationToken);
        await _decoratee.ShutdownAsync(cancellationToken);
    }

    public async ValueTask WriteAsync(
        IReadOnlyList<ReadOnlyMemory<byte>> buffers,
        CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperation.Write, cancellationToken);
        await _decoratee.WriteAsync(buffers, cancellationToken);
    }

    internal TestDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        DuplexTransportOperation holdOperation = DuplexTransportOperation.None,
        DuplexTransportOperation failOperation = DuplexTransportOperation.None,
        Exception? failureException = null)
    {
        _decoratee = decoratee;
        Operations = new(holdOperation, failOperation, failureException);
    }
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
