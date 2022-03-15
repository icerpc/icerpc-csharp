// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Net;

namespace IceRpc.Transports.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class TcpTransportTests
{
    [TestCase(16 * 1024)]
    [TestCase(64 * 1024)]
    [TestCase(256 * 1024)]
    [TestCase(384 * 1024)]
    public void Socket_buffer_size(int bufferSize)
    {
        IClientTransport<ISimpleNetworkConnection> clientTransport =
            new TcpClientTransport(new TcpClientTransportOptions
            {
                ReceiveBufferSize = bufferSize,
                SendBufferSize = bufferSize,
            });

        var connection = (TcpClientNetworkConnection)clientTransport.CreateConnection(
            new Endpoint(Protocol.IceRpc),
            authenticationOptions: null,
            NullLogger.Instance);


        // The OS might allocate more space than the requested size.
        Assert.That(connection.Socket.SendBufferSize, Is.GreaterThanOrEqualTo(bufferSize));
        Assert.That(connection.Socket.ReceiveBufferSize, Is.GreaterThanOrEqualTo(bufferSize));

        // But ensure it doesn't allocate too much as well
        if (OperatingSystem.IsLinux())
        {
            // Linux allocates twice the size.
            Assert.That(connection.Socket.SendBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
            Assert.That(connection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(2.5 * bufferSize));
        }
        else
        {
            // Windows typically allocates the requested size and macOS allocates a little more than the
            // requested size.
            Assert.That(connection.Socket.SendBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
            Assert.That(connection.Socket.ReceiveBufferSize, Is.LessThanOrEqualTo(1.5 * bufferSize));
        }
    }

    [Test]
    public void Socket_is_ipv6_only([Values(true, false)]bool ipv6only)
    {
        IClientTransport<ISimpleNetworkConnection> clientTransport =
            new TcpClientTransport(new TcpClientTransportOptions
            {
                IsIPv6Only = ipv6only
            });

        var connection = (TcpClientNetworkConnection)clientTransport.CreateConnection(
            new Endpoint(Protocol.IceRpc) { Host = "::1"},
            authenticationOptions: null,
            NullLogger.Instance);

        Assert.That(connection.Socket.DualMode, ipv6only ? Is.False : Is.True);
    }

    [Test]
    public void Socket_local_endpoint([Values(true, false)] bool ipv6)
    {
        var localEndpoint = new IPEndPoint(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 10000);
        IClientTransport<ISimpleNetworkConnection> clientTransport =
            new TcpClientTransport(new TcpClientTransportOptions
            {
                LocalEndPoint = localEndpoint,
            });

        var connection = (TcpClientNetworkConnection)clientTransport.CreateConnection(
            new Endpoint(Protocol.IceRpc) { Host = ipv6 ? "::1" : "127.0.0.1" },
            authenticationOptions: null,
            NullLogger.Instance);

        Assert.That(connection.Socket.LocalEndPoint, Is.EqualTo(localEndpoint));
    }
}
