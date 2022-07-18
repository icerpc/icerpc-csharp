// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

[Parallelizable(scope: ParallelScope.All)]
public class ColocTransportTests
{
    [Test]
    public async Task Coloc_transport_connection_information()
    {
        var colocTransport = new ColocTransport();
        Endpoint endpoint = $"icerpc://{Guid.NewGuid()}";
        var listener = colocTransport.ServerTransport.Listen(endpoint, null, NullLogger.Instance);
        var clientConnection = colocTransport.ClientTransport.CreateConnection(endpoint, null, NullLogger.Instance);

        var transportConnectionInformation = await clientConnection.ConnectAsync(default);

        Assert.That(transportConnectionInformation.LocalNetworkAddress, Is.TypeOf<ColocEndPoint>());
        var endPoint = (ColocEndPoint?)transportConnectionInformation.LocalNetworkAddress;
        Assert.That(endPoint?.ToString(), Is.EqualTo(listener.Endpoint.ToString()));
        Assert.That(transportConnectionInformation.RemoteNetworkAddress, Is.TypeOf<ColocEndPoint>());
        endPoint = (ColocEndPoint?)transportConnectionInformation.RemoteNetworkAddress;
        Assert.That(endPoint?.ToString(), Is.EqualTo(listener.Endpoint.ToString()));
        Assert.That(transportConnectionInformation.RemoteCertificate, Is.Null);
    }
}
