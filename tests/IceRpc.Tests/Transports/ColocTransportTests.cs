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
        var listener = colocTransport.ServerTransport.Listen(serverAddress, new DuplexConnectionOptions(), null);
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
}
