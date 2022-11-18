// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

[Parallelizable(scope: ParallelScope.All)]
public class ColocTransportTests
{
    [Test]
    public async Task Coloc_transport_connection_information()
    {
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        Task<TransportConnectionInformation> connectTask = clientConnection.ConnectAsync(default);
        using IDuplexConnection _ = (await listener.AcceptAsync(default)).Connection;

        // Act
        TransportConnectionInformation transportConnectionInformation = await connectTask;

        // Assert
        Assert.That(transportConnectionInformation.LocalNetworkAddress, Is.TypeOf<ColocEndPoint>());
        var localNetworkAddress = (ColocEndPoint?)transportConnectionInformation.LocalNetworkAddress;
        Assert.That(localNetworkAddress?.ToString(), Is.EqualTo(listener.ServerAddress.ToString()));
        Assert.That(transportConnectionInformation.RemoteNetworkAddress, Is.TypeOf<ColocEndPoint>());

        var remoteNetworkAddress = (ColocEndPoint?)transportConnectionInformation.RemoteNetworkAddress;

        Assert.That(remoteNetworkAddress?.ToString(), Is.EqualTo(listener.ServerAddress.ToString()));
        Assert.That(transportConnectionInformation.RemoteCertificate, Is.Null);
    }

    [TestCase(1)]
    [TestCase(10)]
    public async Task Coloc_transport_listener_backlog(int listenBacklog)
    {
        var colocTransport = new ColocTransport(listenBacklog);
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        // Fill the pending connection queue.
        var connections = new List<(IDuplexConnection, Task)>();
        for (int i = 0; i < listenBacklog; ++i)
        {
            IDuplexConnection connection = colocTransport.ClientTransport.CreateConnection(
                serverAddress,
                new DuplexConnectionOptions(),
                null);
            connections.Add((connection, connection.ConnectAsync(default)));
        }

        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        // Act/Assert
        Assert.That(() => clientConnection.ConnectAsync(default), Throws.InstanceOf<TransportException>());
        await listener.DisposeAsync();
        foreach ((IDuplexConnection connection, Task connectTask) in connections)
        {
            Assert.That(() => connectTask, Throws.InstanceOf<TransportException>());
            connection.Dispose();
        }
    }
}
