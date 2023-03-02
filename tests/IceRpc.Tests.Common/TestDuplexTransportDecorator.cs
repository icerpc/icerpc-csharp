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

/// <summary>A property bag used to configure a <see cref="TransportOperations{DuplexTransportOperations}" />.</summary>
public record class DuplexTransportOperationsOptions : TransportOperationsOptions<DuplexTransportOperations>
{
    /// <summary>The connection read decorator.</summary>
    public Func<IDuplexConnection, Memory<byte>, CancellationToken, ValueTask<int>>? ReadDecorator { get; set; }
}

/// <summary>A <see cref="IDuplexClientTransport" /> decorator to create decorated <see cref="IDuplexConnection" />
/// client connections and to get to the last created connection.</summary>
public sealed class TestDuplexClientTransportDecorator : IDuplexClientTransport
{
    /// <summary>The operations options used to create client connections.</summary>
    public DuplexTransportOperationsOptions ConnectionOperationsOptions { get; set; }

    /// <summary>The last created connection.</summary>
    public TestDuplexConnectionDecorator LastCreatedConnection =>
        _lastConnection ?? throw new InvalidOperationException("Call CreateConnection first.");

    /// <inheritdoc/>
    public string Name => _decoratee.Name;

    private readonly IDuplexClientTransport _decoratee;
    private TestDuplexConnectionDecorator? _lastConnection;

    /// <summary>Constructs a <see cref="TestDuplexClientTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated client transport.</param>
    /// <param name="operationsOptions">The connection operations options.</param>
    public TestDuplexClientTransportDecorator(
        IDuplexClientTransport decoratee,
        DuplexTransportOperationsOptions? operationsOptions = null)
    {
        _decoratee = decoratee;
        ConnectionOperationsOptions = operationsOptions ?? new();
    }

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
}

/// <summary>A <see cref="IDuplexServerTransport" /> decorator to create decorated <see cref="IDuplexConnection" />
/// server connections and to get the last accepted connection.</summary>
#pragma warning disable CA1001 // _listener is disposed by Listen caller.
public class TestDuplexServerTransportDecorator : IDuplexServerTransport
#pragma warning restore CA1001
{
    /// <summary>The operations options used to create server connections.</summary>
    public DuplexTransportOperationsOptions ConnectionOperationsOptions
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

    private DuplexTransportOperationsOptions _connectionOperationsOptions;
    private readonly IDuplexServerTransport _decoratee;
    private TestDuplexListenerDecorator? _listener;

    /// <summary>Constructs a <see cref="TestDuplexServerTransportDecorator" />.</summary>
    /// <param name="decoratee">The decorated server transport.</param>
    /// <param name="operationsOptions">The transport operations options.</param>
    public TestDuplexServerTransportDecorator(
        IDuplexServerTransport decoratee,
        DuplexTransportOperationsOptions? operationsOptions = null)
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
        public DuplexTransportOperationsOptions ConnectionOperationsOptions { get; set; }

        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        internal TestDuplexConnectionDecorator LastAcceptedConnection
        {
            get => _lastAcceptedConnection ?? throw new InvalidOperationException("Call AcceptAsync first.");
            private set => _lastAcceptedConnection = value;
        }

        private readonly IListener<IDuplexConnection> _decoratee;
        private TestDuplexConnectionDecorator? _lastAcceptedConnection;
        private readonly TransportOperations<DuplexTransportOperations> _listenerOperations;

        public Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken) =>
            _listenerOperations.CallAsync(
                DuplexTransportOperations.Accept,
                async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> () =>
                {
                    (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                        await _decoratee.AcceptAsync(cancellationToken);
                    LastAcceptedConnection = new TestDuplexConnectionDecorator(
                        connection,
                        ConnectionOperationsOptions);
                    if (_listenerOperations.Fail.HasFlag(DuplexTransportOperations.Accept))
                    {
                        // Dispose the connection if the operation is configured to fail.
                        connection.Dispose();
                    }
                    return (LastAcceptedConnection, remoteNetworkAddress);
                },
                cancellationToken);

        public ValueTask DisposeAsync() =>
            _listenerOperations.CallDisposeAsync(DuplexTransportOperations.Dispose, _decoratee);

        internal TestDuplexListenerDecorator(
            IListener<IDuplexConnection> decoratee,
            TransportOperations<DuplexTransportOperations> listenerOperations,
            DuplexTransportOperationsOptions operationsOptions)
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
    private readonly Func<IDuplexConnection, Memory<byte>, CancellationToken, ValueTask<int>>? _readDecorator;

    /// <inheritdoc/>
    public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
        Operations.CallAsync(
            DuplexTransportOperations.Connect,
            () => _decoratee.ConnectAsync(cancellationToken),
            cancellationToken);

    /// <inheritdoc/>
    public void Dispose() => Operations.CallDispose(DuplexTransportOperations.Dispose, _decoratee);

    /// <inheritdoc/>
    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        Operations.CallAsync(
            DuplexTransportOperations.Read,
            () => _readDecorator == null ?
                _decoratee.ReadAsync(buffer, cancellationToken) :
                _readDecorator(_decoratee, buffer, cancellationToken),
            cancellationToken);

    /// <inheritdoc/>
    public Task ShutdownAsync(CancellationToken cancellationToken) =>
        Operations.CallAsync(
            DuplexTransportOperations.Shutdown,
            () => _decoratee.ShutdownAsync(cancellationToken),
            cancellationToken);

    /// <inheritdoc/>
    public ValueTask WriteAsync(IReadOnlyList<ReadOnlyMemory<byte>> buffers, CancellationToken cancellationToken) =>
        Operations.CallAsync(
            DuplexTransportOperations.Write,
            () => _decoratee.WriteAsync(buffers, cancellationToken),
            cancellationToken);

    internal TestDuplexConnectionDecorator(
        IDuplexConnection decoratee,
        DuplexTransportOperationsOptions operationsOptions)
    {
        _decoratee = decoratee;
        _readDecorator = operationsOptions.ReadDecorator;
        Operations = new(operationsOptions);
    }
}

/// <summary>Extension methods for setting up the test duplex transport in an <see cref="IServiceCollection"
/// />.</summary>
public static class TestDuplexTransportDecoratorServiceCollectionExtensions
{
    /// <summary>Installs the test duplex transport decorator.</summary>
    public static IServiceCollection AddTestDuplexTransportDecorator(
        this IServiceCollection services,
        DuplexTransportOperationsOptions? clientOperationsOptions = null,
        DuplexTransportOperationsOptions? serverOperationsOptions = null)
    {
        services.AddSingletonDecorator<IDuplexClientTransport, TestDuplexClientTransportDecorator>(
            services.Last(descriptor => descriptor.ServiceType == typeof(IDuplexClientTransport)),
            clientTransport => new TestDuplexClientTransportDecorator(clientTransport, clientOperationsOptions));
        services.AddSingletonDecorator<IDuplexServerTransport, TestDuplexServerTransportDecorator>(
            services.Last(descriptor => descriptor.ServiceType == typeof(IDuplexServerTransport)),
            serverTransport => new TestDuplexServerTransportDecorator(serverTransport, serverOperationsOptions));
        return services;
    }
}
