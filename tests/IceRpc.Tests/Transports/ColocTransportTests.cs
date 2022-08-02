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
        ServerAddress serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        var listener = colocTransport.ServerTransport.Listen(serverAddress, new DuplexConnectionOptions(), null);
        var clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        var transportConnectionInformation = await clientConnection.ConnectAsync(default);

        Assert.That(transportConnectionInformation.LocalNetworkAddress, Is.TypeOf<ColocEndPoint>());
        var endPoint = (ColocEndPoint?)transportConnectionInformation.LocalNetworkAddress;
        Assert.That(endPoint?.ToString(), Is.EqualTo(listener.ServerAddress.ToString()));
        Assert.That(transportConnectionInformation.RemoteNetworkAddress, Is.TypeOf<ColocEndPoint>());
        endPoint = (ColocEndPoint?)transportConnectionInformation.RemoteNetworkAddress;
        Assert.That(endPoint?.ToString(), Is.EqualTo(listener.ServerAddress.ToString()));
        Assert.That(transportConnectionInformation.RemoteCertificate, Is.Null);
    }
}
