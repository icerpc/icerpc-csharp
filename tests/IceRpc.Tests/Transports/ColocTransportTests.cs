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
    public async Task Coloc_network_connection_information()
    {
        var colocTransport = new ColocTransport();
        var endpoint = $"icerpc://{Guid.NewGuid()}";
        var listener = colocTransport.ServerTransport.Listen(endpoint, null, NullLogger.Instance);
        var clientConnection = colocTransport.ClientTransport.CreateConnection(endpoint, null, NullLogger.Instance);

        var networkConnectionInformation = await clientConnection.ConnectAsync(default);

        Assert.That(networkConnectionInformation.LocalEndPoint, Is.TypeOf<ColocEndPoint>());
        var endPoint = (ColocEndPoint)networkConnectionInformation.LocalEndPoint;
        Assert.That(endPoint.ToString(), Is.EqualTo(listener.Endpoint.ToString()));
        Assert.That(networkConnectionInformation.RemoteEndPoint, Is.TypeOf<ColocEndPoint>());
        endPoint = (ColocEndPoint)networkConnectionInformation.RemoteEndPoint;
        Assert.That(endPoint.ToString(), Is.EqualTo(listener.Endpoint.ToString()));
        Assert.That(networkConnectionInformation.IdleTimeout, Is.EqualTo(TimeSpan.MaxValue));
        Assert.That(networkConnectionInformation.RemoteCertificate, Is.Null);
    }
}
