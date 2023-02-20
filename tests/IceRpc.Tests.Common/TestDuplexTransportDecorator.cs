// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests.Common;

/// <summary>This enumeration describes the duplex transport operations.</summary>
[Flags]
public enum DuplexTransportOperations
{
    /// <summary>The no-operation enumerator value.</summary>
    None = 0,

    /// <summary>The <see cref="IListener{IDuplexConnection}.AcceptAsync" /> operation.</summary>
    Accept = 1,

    /// <summary>The <see cref="IDuplexConnection.ConnectAsync" /> operation.</summary>
    Connect = 2,

    /// <summary>The <see cref="IDisposable.Dispose" /> operation.</summary>
    Dispose = 4,

    /// <summary>The <see cref="IDuplexConnection.ReadAsync" /> operation.</summary>
    Read = 8,

    /// <summary>The <see cref="IDuplexConnection.ShutdownAsync" /> operation.</summary>
    Shutdown = 16,

    /// <summary>The <see cref="IDuplexConnection.WriteAsync" /> operation.</summary>
    Write = 32,
}

/// <summary>A <see cref="IDuplexClientTransport" /> decorator to create decorated <see cref="IDuplexConnection" />
/// client connections and to get to the last created connection.</summary>
public sealed class TestDuplexClientTransportDecorator : IDuplexClientTransport
{
    /// <summary>The operations options used to create client connections.</summary>
    public TransportOperationsOptions<DuplexTransportOperations> ConnectionOperationsOptions { get; set; }

    /// <summary>The last created connection.</summary>
    public TestDuplexConnectionDecorator LastCreatedConnection =>
        _lastConnection ?? throw new InvalidOperationException("Call CreateConnection first.");

    /// <inheritdoc/>
    public string Name => _decoratee.Name;

    private readonly IDuplexClientTransport _decoratee;
    private TestDuplexConnectionDecorator? _lastConnection;

    /// <inheritdoc/>
    public bool CheckParams(ServerAddress serverAddress) => _decoratee.CheckParams(serverAddress);

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        var connection = new TestDuplexConnectionDecorator(
            _decoratee.CreateConnection(serverAddress, options, clientAuthenticationOptions),
            ConnectionOperationsOptions);
        _lastConnection = connection;
        return connection;
    }

    /// <summary>Constructs a <see cref="TestDuplexClientTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated client transport.</param>
    /// <param name="operationsOptions">The connection operations options.</param>
    public TestDuplexClientTransportDecorator(
        IDuplexClientTransport decoratee,
        TransportOperationsOptions<DuplexTransportOperations>? operationsOptions = null)
    {
        _decoratee = decoratee;
        ConnectionOperationsOptions = operationsOptions ?? new();
    }
}

/// <summary>A <see cref="IDuplexServerTransport" /> decorator to create decorated <see cref="IDuplexConnection" />
/// server connections and to get the last accepted connection.</summary>
#pragma warning disable CA1001 // _listener is disposed by Listen caller.
public class TestDuplexServerTransportDecorator : IDuplexServerTransport
#pragma warning restore CA1001
{
    /// <summary>The operations options used to create server connections.</summary>
    public TransportOperationsOptions<DuplexTransportOperations> ConnectionOperationsOptions
    {
        get => _listener?.ConnectionOperationsOptions ?? _connectionOperationsOptions;

        set
        {
            if (_listener is null)
            {
                _connectionOperationsOptions = value;
            }
            else
            {
                _listener.ConnectionOperationsOptions = value;
            }
        }
    }

    /// <summary>The last accepted connection.</summary>
    public TestDuplexConnectionDecorator LastAcceptedConnection =>
        _listener?.LastAcceptedConnection ?? throw new InvalidOperationException("Call Listen first.");

    /// <inheritdoc/>
    public string Name => _decoratee.Name;

    /// <summary>The <see cref="TransportOperations{DuplexTransportOperations}" /> used by the <see
    /// cref="IListener{IDuplexConnection}" /> operations.</summary>
    public TransportOperations<DuplexTransportOperations> ListenerOperations;

    private TransportOperationsOptions<DuplexTransportOperations> _connectionOperationsOptions;
    private readonly IDuplexServerTransport _decoratee;
    private TestDuplexListenerDecorator? _listener;

    /// <summary>Constructs a <see cref="TestDuplexServerTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated server transport.</param>
    /// <param name="operationsOptions">The transport operations options.</param>
    public TestDuplexServerTransportDecorator(
        IDuplexServerTransport decoratee,
        TransportOperationsOptions<DuplexTransportOperations>? operationsOptions = null)
    {
        _decoratee = decoratee;
        _connectionOperationsOptions = operationsOptions ?? new();
        ListenerOperations = new(_connectionOperationsOptions);
    }

    /// <inheritdoc/>
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
            ListenerOperations,
            _connectionOperationsOptions);

        return _listener;
    }

    private class TestDuplexListenerDecorator : IListener<IDuplexConnection>
    {
        public TransportOperationsOptions<DuplexTransportOperations> ConnectionOperationsOptions { get; set; }

        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        internal TestDuplexConnectionDecorator LastAcceptedConnection
        {
            get => _lastAcceptedConnection ?? throw new InvalidOperationException("Call AcceptAsync first.");
            private set => _lastAcceptedConnection = value;
        }

        private readonly IListener<IDuplexConnection> _decoratee;
        private TestDuplexConnectionDecorator? _lastAcceptedConnection;
        private readonly TransportOperations<DuplexTransportOperations> _listenerOperations;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            await _listenerOperations.CheckAsync(DuplexTransportOperations.Accept, cancellationToken);

            (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await _listenerOperations.CheckAsync(DuplexTransportOperations.Accept, cancellationToken);
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            LastAcceptedConnection = new TestDuplexConnectionDecorator(connection, ConnectionOperationsOptions);
            return (LastAcceptedConnection, remoteNetworkAddress);
        }

        public async ValueTask DisposeAsync()
        {
            await _listenerOperations.CheckAsync(DuplexTransportOperations.Dispose, CancellationToken.None);
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            _listenerOperations.Complete();
        }

        internal TestDuplexListenerDecorator(
            IListener<IDuplexConnection> decoratee,
            TransportOperations<DuplexTransportOperations> listenerOperations,
            TransportOperationsOptions<DuplexTransportOperations> operationsOptions)
        {
            _decoratee = decoratee;
            _listenerOperations = listenerOperations;
            ConnectionOperationsOptions = operationsOptions;
        }
    }
}

/// <summary>An <see cref="IDuplexConnection" /> decorator to configure the behavior of connection operations.</summary>
public sealed class TestDuplexConnectionDecorator : IDuplexConnection
{
    /// <summary>The <see cref="TransportOperations{DuplexTransportOperation}" /> used by this connection
    /// operations.</summary>
    public TransportOperations<DuplexTransportOperations> Operations { get; }

    private readonly IDuplexConnection _decoratee;

    /// <inheritdoc/>
    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperations.Connect, cancellationToken);
        return await _decoratee.ConnectAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Operations.Called(DuplexTransportOperations.Dispose);
        _decoratee.Dispose();
        Operations.Complete();
    }

    /// <inheritdoc/>
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperations.Read, cancellationToken);

        int count = await _decoratee.ReadAsync(buffer, cancellationToken);

        // Check again fail/hold condition in case the configuration was changed while ReadAsync was pending.
        await Operations.CheckAsync(DuplexTransportOperations.Read, cancellationToken);

        return count;
    }

    /// <inheritdoc/>
    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperations.Shutdown, cancellationToken);
        await _decoratee.ShutdownAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(
        IReadOnlyList<ReadOnlyMemory<byte>> buffers,
        CancellationToken cancellationToken)
    {
        await Operations.CheckAsync(DuplexTransportOperations.Write, cancellationToken);
        await _decoratee.WriteAsync(buffers, cancellationToken);
    }

    internal TestDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        TransportOperationsOptions<DuplexTransportOperations> operationOptions)
    {
        _decoratee = decoratee;
        Operations = new(operationOptions);
    }
}

/// <summary>Extension methods for setting up the test duplex transport in an <see cref="IServiceCollection"
/// />.</summary>
public static class TestDuplexTransportServiceCollectionExtensions
{
    /// <summary>Installs the test duplex transport.</summary>
    public static IServiceCollection AddTestDuplexTransport(
        this IServiceCollection services,
        TransportOperationsOptions<DuplexTransportOperations>? clientOperationsOptions = null,
        TransportOperationsOptions<DuplexTransportOperations>? serverOperationsOptions = null) => services
        .AddSingleton<ColocTransportOptions>()
        .AddSingleton(provider => new ColocTransport(provider.GetRequiredService<ColocTransportOptions>()))
        .AddSingleton(provider =>
            new TestDuplexClientTransportDecorator(
                provider.GetRequiredService<ColocTransport>().ClientTransport,
                clientOperationsOptions))
        .AddSingleton<IDuplexClientTransport>(provider =>
            provider.GetRequiredService<TestDuplexClientTransportDecorator>())
        .AddSingleton(provider =>
            new TestDuplexServerTransportDecorator(
                provider.GetRequiredService<ColocTransport>().ServerTransport,
                serverOperationsOptions))
        .AddSingleton<IDuplexServerTransport>(provider =>
            provider.GetRequiredService<TestDuplexServerTransportDecorator>());
}
