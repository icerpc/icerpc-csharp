// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Coloc;
using IceRpc.Transports.Slic;
using NUnit.Framework;
using System.Buffers;
using System.Net;
using System.Net.Security;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class ProtocolLoggerTests
{
    private static List<Protocol> Protocols => new() { Protocol.IceRpc, Protocol.Ice };

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Log_connection_accepted_and_connection_connected(Protocol protocol)
    {
        // Arrange
        var serverAddress = new ServerAddress(new Uri($"{protocol}://colochost-{Guid.NewGuid()}"));
        using var serverLoggerFactory = new TestLoggerFactory();
        using var clientLoggerFactory = new TestLoggerFactory();
        var colocTransport = new ColocTransport();
        await using var server = new Server(
            dispatcher: NotFoundDispatcher.Instance,
            serverAddress,
            duplexServerTransport: colocTransport.ServerTransport,
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport),
            logger: serverLoggerFactory.CreateLogger("IceRpc"));
        serverAddress = server.Listen();

        await using var clientConnection = new ClientConnection(
            serverAddress,
            duplexClientTransport: colocTransport.ClientTransport,
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport),
            logger: clientLoggerFactory.CreateLogger("IceRpc"));
        using var request = new OutgoingRequest(
            new ServiceAddress(protocol)
            {
                ServerAddress = serverAddress
            });

        // Act
        var clientConnectionInformation = await clientConnection.ConnectAsync();
        // Send a request to ensure the server side is connected before than we inspect the log
        _ = await clientConnection.InvokeAsync(request);

        // Assert
        Assert.That(serverLoggerFactory.Logger, Is.Not.Null);
        TestLoggerEntry entry = await serverLoggerFactory.Logger!.Entries.Reader.ReadAsync();

        Assert.That(entry.EventId.Id, Is.EqualTo((int)ProtocolEventIds.StartAcceptingConnections));
        Assert.That(entry.State["ServerAddress"], Is.EqualTo(serverAddress));

        entry = await serverLoggerFactory.Logger!.Entries.Reader.ReadAsync();
        Assert.That(entry.EventId.Id, Is.EqualTo((int)ProtocolEventIds.ConnectionAccepted));
        Assert.That(entry.State["ServerAddress"], Is.EqualTo(serverAddress));
        Assert.That(
            entry.State["RemoteNetworkAddress"]?.ToString(),
            Is.EqualTo(clientConnectionInformation.LocalNetworkAddress.ToString()));

        entry = await serverLoggerFactory.Logger!.Entries.Reader.ReadAsync();
        Assert.That(entry.EventId.Id, Is.EqualTo((int)ProtocolEventIds.ConnectionConnected));
        Assert.That(entry.State["Kind"], Is.EqualTo("Server"));
        Assert.That(
            entry.State["LocalNetworkAddress"]?.ToString(),
            Is.EqualTo(clientConnectionInformation.RemoteNetworkAddress.ToString()));
        Assert.That(
            entry.State["RemoteNetworkAddress"]?.ToString(),
            Is.EqualTo(clientConnectionInformation.LocalNetworkAddress.ToString()));

        Assert.That(clientLoggerFactory.Logger, Is.Not.Null);
        entry = await clientLoggerFactory.Logger!.Entries.Reader.ReadAsync();

        Assert.That(entry.EventId.Id, Is.EqualTo((int)ProtocolEventIds.ConnectionConnected));
        Assert.That(entry.State["Kind"], Is.EqualTo("Client"));
        Assert.That(
            entry.State["LocalNetworkAddress"],
            Is.EqualTo(clientConnectionInformation.LocalNetworkAddress));
        Assert.That(
            entry.State["RemoteNetworkAddress"],
            Is.EqualTo(clientConnectionInformation.RemoteNetworkAddress));
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Log_connection_connected_failed(Protocol protocol)
    {
        // Arrange
        var serverAddress = new ServerAddress(new Uri($"{protocol}://colochost-{Guid.NewGuid()}"));
        using var serverLoggerFactory = new TestLoggerFactory();
        using var clientLoggerFactory = new TestLoggerFactory();
        var colocTransport = new ColocTransport();
        await using var server = new Server(
            dispatcher: NotFoundDispatcher.Instance,
            serverAddress,
            duplexServerTransport: new ConnectFailDuplexServerTransportDecorator(
                colocTransport.ServerTransport),
            multiplexedServerTransport: new ConnectFailMultiplexedServerTransportDecorator(
                new SlicServerTransport(colocTransport.ServerTransport)),
            logger: serverLoggerFactory.CreateLogger("IceRpc"));
        serverAddress = server.Listen();

        await using var clientConnection = new ClientConnection(
            serverAddress,
            duplexClientTransport: colocTransport.ClientTransport,
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport),
            logger: clientLoggerFactory.CreateLogger("IceRpc"));

        // Act
        Assert.ThrowsAsync<IceRpcException>(async () => await clientConnection.ConnectAsync(default));

        // Assert
        Assert.That(serverLoggerFactory.Logger, Is.Not.Null);
        TestLoggerEntry? entry;
        do
        {
            entry = await serverLoggerFactory.Logger!.Entries.Reader.ReadAsync();
        }
        while (entry.EventId != (int)ProtocolEventIds.ConnectionConnectFailed);

        Assert.That(entry.EventId.Id, Is.EqualTo((int)ProtocolEventIds.ConnectionConnectFailed));
        Assert.That(entry.State["ServerAddress"], Is.EqualTo(serverAddress));
        Assert.That(entry.Exception, Is.InstanceOf<InvalidOperationException>());

        Assert.That(clientLoggerFactory.Logger, Is.Not.Null);
        entry = await clientLoggerFactory.Logger!.Entries.Reader.ReadAsync();

        Assert.That(entry.EventId.Id, Is.EqualTo((int)ProtocolEventIds.ConnectionConnectFailed));
        Assert.That(entry.State["ServerAddress"], Is.EqualTo(serverAddress));
        Assert.That(entry.Exception, Is.InstanceOf<IceRpcException>());
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Log_connection_dispose_without_shutdown(Protocol protocol)
    {
        // Arrange
        var serverAddress = new ServerAddress(new Uri($"{protocol}://colochost-{Guid.NewGuid()}"));
        using var serverLoggerFactory = new TestLoggerFactory();
        using var clientLoggerFactory = new TestLoggerFactory();
        using var dispatcher = new TestDispatcher(holdDispatchCount: 1);
        var colocTransport = new ColocTransport();
        await using var server = new Server(
            dispatcher,
            serverAddress,
            duplexServerTransport: colocTransport.ServerTransport,
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport),
            logger: serverLoggerFactory.CreateLogger("IceRpc"));
        serverAddress = server.Listen();

        await using var clientConnection = new ClientConnection(
            serverAddress,
            duplexClientTransport: colocTransport.ClientTransport,
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport),
            logger: clientLoggerFactory.CreateLogger("IceRpc"));
        using var request = new OutgoingRequest(
            new ServiceAddress(protocol)
            {
                ServerAddress = serverAddress
            });
        // Act
        var clientConnectionInformation = await clientConnection.ConnectAsync();
        var invokeTask = clientConnection.InvokeAsync(request, default);
        await dispatcher.DispatchStart;
        await clientConnection.DisposeAsync();

        // Assert
        Assert.ThrowsAsync<IceRpcException>(async () => await invokeTask);
        Assert.That(serverLoggerFactory.Logger, Is.Not.Null);

        TestLoggerEntry? entry;
        do
        {
            entry = await serverLoggerFactory.Logger!.Entries.Reader.ReadAsync();
        }
        while (entry.EventId != (int)ProtocolEventIds.ConnectionShutdownFailed);

        Assert.That(entry, Is.Not.Null);
        Assert.That(entry.State["Kind"], Is.EqualTo("Server"));
        Assert.That(entry.State["LocalNetworkAddress"]?.ToString(),
            Is.EqualTo(clientConnectionInformation.RemoteNetworkAddress.ToString()));
        Assert.That(
            entry.State["RemoteNetworkAddress"]?.ToString(),
            Is.EqualTo(clientConnectionInformation.LocalNetworkAddress.ToString()));
        Assert.That(entry.Exception, Is.InstanceOf<IceRpcException>()); // the shutdown failure

        Assert.That(clientLoggerFactory.Logger, Is.Not.Null);
        do
        {
            entry = await clientLoggerFactory.Logger!.Entries.Reader.ReadAsync();
        }
        while (entry.EventId != (int)ProtocolEventIds.ConnectionDisposed);

        Assert.That(entry.State["Kind"], Is.EqualTo("Client"));
        Assert.That(
            entry.State["LocalNetworkAddress"],
            Is.EqualTo(clientConnectionInformation.LocalNetworkAddress));
        Assert.That(
            entry.State["RemoteNetworkAddress"],
            Is.EqualTo(clientConnectionInformation.RemoteNetworkAddress));
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Log_start_stop_accept(Protocol protocol)
    {
        // Arrange
        var serverAddress = new ServerAddress(new Uri($"{protocol}://colochost-{Guid.NewGuid()}"));
        using var loggerFactory = new TestLoggerFactory();
        var colocTransport = new ColocTransport();
        var server = new Server(
            dispatcher: NotFoundDispatcher.Instance,
            serverAddress,
            duplexServerTransport: colocTransport.ServerTransport,
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport),
            logger: loggerFactory.CreateLogger("IceRpc"));

        // Act
        serverAddress = server.Listen();
        await server.DisposeAsync();

        // Assert
        Assert.That(loggerFactory.Logger, Is.Not.Null);
        TestLoggerEntry entry = await loggerFactory.Logger!.Entries.Reader.ReadAsync();

        Assert.That(entry.EventId.Id, Is.EqualTo((int)ProtocolEventIds.StartAcceptingConnections));
        Assert.That(entry.State["ServerAddress"], Is.EqualTo(serverAddress));

        entry = await loggerFactory.Logger!.Entries.Reader.ReadAsync();
        Assert.That(entry.EventId.Id, Is.EqualTo((int)ProtocolEventIds.StopAcceptingConnections));
        Assert.That(entry.State["ServerAddress"], Is.EqualTo(serverAddress));
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Log_connection_shutdown(Protocol protocol)
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"{protocol}://colochost-{Guid.NewGuid()}"));
        using var serverLoggerFactory = new TestLoggerFactory();
        using var clientLoggerFactory = new TestLoggerFactory();

        await using var server = new Server(
            dispatcher: NotFoundDispatcher.Instance,
            serverAddress,
            duplexServerTransport: colocTransport.ServerTransport,
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport),
            logger: serverLoggerFactory.CreateLogger("IceRpc"));
        serverAddress = server.Listen();

        await using var clientConnection = new ClientConnection(
            serverAddress,
            duplexClientTransport: colocTransport.ClientTransport,
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport),
            logger: clientLoggerFactory.CreateLogger("IceRpc"));

        var clientConnectionInformation = await clientConnection.ConnectAsync(default);

        {
            using var request = new OutgoingRequest(
                new ServiceAddress(protocol)
                {
                    ServerAddress = serverAddress
                });

            // Send a request to ensure the server side is connected before than we shutdown the connection
            _ = await clientConnection.InvokeAsync(request);
        }

        // Act
        await clientConnection.ShutdownAsync();

        // Assert
        Assert.That(serverLoggerFactory.Logger, Is.Not.Null);
        var entries = serverLoggerFactory.Logger!.Entries;
        TestLoggerEntry? entry = null;
        do
        {
            entry = await serverLoggerFactory.Logger.Entries.Reader.ReadAsync();
        }
        while (entry.EventId.Id != (int)ProtocolEventIds.ConnectionShutdown);

        Assert.That(entry.State["Kind"], Is.EqualTo("Server"));
        Assert.That(
            entry.State["LocalNetworkAddress"]?.ToString(),
            Is.EqualTo(clientConnectionInformation.RemoteNetworkAddress.ToString()));
        Assert.That(
            entry.State["RemoteNetworkAddress"]?.ToString(),
            Is.EqualTo(clientConnectionInformation.LocalNetworkAddress.ToString()));

        Assert.That(clientLoggerFactory.Logger, Is.Not.Null);
        entries = clientLoggerFactory.Logger!.Entries;

        do
        {
            entry = await clientLoggerFactory.Logger.Entries.Reader.ReadAsync();
        }
        while (entry.EventId.Id != (int)ProtocolEventIds.ConnectionShutdown);
        Assert.That(entry.EventId.Id, Is.EqualTo((int)ProtocolEventIds.ConnectionShutdown));
        Assert.That(entry.State["Kind"], Is.EqualTo("Client"));
        Assert.That(
            entry.State["LocalNetworkAddress"],
            Is.EqualTo(clientConnectionInformation.RemoteNetworkAddress));
        Assert.That(
            entry.State["RemoteNetworkAddress"],
            Is.EqualTo(clientConnectionInformation.LocalNetworkAddress));
    }

    // A duplex server transport decorators that wraps the listeners it creates with a listener
    // that creates duplex connections that will fail during connect.
    internal sealed class ConnectFailDuplexServerTransportDecorator : IDuplexServerTransport
    {
        public string Name => _decoratee.Name;

        private readonly IDuplexServerTransport _decoratee;

        internal ConnectFailDuplexServerTransportDecorator(IDuplexServerTransport decoratee) =>
            _decoratee = decoratee;

        public IListener<IDuplexConnection> Listen(
            ServerAddress serverAddress,
            DuplexConnectionOptions options,
            SslServerAuthenticationOptions? serverAuthenticationOptions)
        {
            IListener<IDuplexConnection> listener =
                _decoratee.Listen(serverAddress, options, serverAuthenticationOptions);
            return new ConnectFailDuplexConnectionListenerDecorator(listener);
        }
    }

    // A duplex listener decorator that wraps the connections it creates with a connection decorator
    // that will fail to connect
    private sealed class ConnectFailDuplexConnectionListenerDecorator : IListener<IDuplexConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IListener<IDuplexConnection> _decoratee;

        public async Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IDuplexConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken);
            return (new ConnectFailDuplexConnectionDecorator(connection), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal ConnectFailDuplexConnectionListenerDecorator(IListener<IDuplexConnection> decoratee) =>
            _decoratee = decoratee;
    }

    // A duplex connection decorator that always fail to connect
    private sealed class ConnectFailDuplexConnectionDecorator : IDuplexConnection
    {
        private readonly IDuplexConnection _decoratee;

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Connect failed.");

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            _decoratee.ReadAsync(buffer, cancellationToken);

        public Task ShutdownWriteAsync(CancellationToken cancellationToken) =>
            _decoratee.ShutdownWriteAsync(cancellationToken);

        public ValueTask WriteAsync(ReadOnlySequence<byte> buffer, CancellationToken cancellationToken) =>
            _decoratee.WriteAsync(buffer, cancellationToken);

        public void Dispose() => _decoratee.Dispose();

        internal ConnectFailDuplexConnectionDecorator(IDuplexConnection decoratee) => _decoratee = decoratee;
    }

    // A multiplexed server transport decorators that wraps the listeners it creates with a listener
    // that creates multiplexed connections that will fail during connect.
    internal sealed class ConnectFailMultiplexedServerTransportDecorator : IMultiplexedServerTransport
    {
        public string Name => _decoratee.Name;

        private readonly IMultiplexedServerTransport _decoratee;

        internal ConnectFailMultiplexedServerTransportDecorator(IMultiplexedServerTransport decoratee) =>
            _decoratee = decoratee;

        public IListener<IMultiplexedConnection> Listen(
            ServerAddress serverAddress,
            MultiplexedConnectionOptions options,
            SslServerAuthenticationOptions? serverAuthenticationOptions)
        {
            IListener<IMultiplexedConnection> listener =
                _decoratee.Listen(serverAddress, options, serverAuthenticationOptions);
            return new ConnectFailMultiplexedConnectionListenerDecorator(listener);
        }
    }

    // A multiplexed listener decorator that wraps the connections it creates with a connection decorator
    // that will fail to connect
    private sealed class ConnectFailMultiplexedConnectionListenerDecorator : IListener<IMultiplexedConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IListener<IMultiplexedConnection> _decoratee;

        public async Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync(
            CancellationToken cancellationToken)
        {
            (IMultiplexedConnection connection, EndPoint remoteNetworkAddress) =
                await _decoratee.AcceptAsync(cancellationToken);
            return (new ConnectFailMultiplexedConnectionDecorator(connection), remoteNetworkAddress);
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal ConnectFailMultiplexedConnectionListenerDecorator(IListener<IMultiplexedConnection> decoratee) =>
            _decoratee = decoratee;
    }

    // A multiplexed connection decorator that always fail to connect
    private sealed class ConnectFailMultiplexedConnectionDecorator : IMultiplexedConnection
    {
        private readonly IMultiplexedConnection _decoratee;

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Connect failed.");

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();
        public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken) =>
            _decoratee.AcceptStreamAsync(cancellationToken);

        public Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken) =>
            _decoratee.CloseAsync(closeError, cancellationToken);

        public ValueTask<IMultiplexedStream> CreateStreamAsync(
            bool bidirectional,
            CancellationToken cancellationToken) =>
            _decoratee.CreateStreamAsync(bidirectional, cancellationToken);

        internal ConnectFailMultiplexedConnectionDecorator(IMultiplexedConnection decoratee) => _decoratee = decoratee;
    }
}
