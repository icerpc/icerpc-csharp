// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace IceRpc.Tests.Transports.Tcp;

/// <summary>Conformance tests for the tcp transport.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpConnectionConformanceTests : DuplexConnectionConformanceTests
{
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddTcpTest(listenBacklog);
}

/// <summary>Conformance tests for the tcp transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpListenerConformanceTests : DuplexListenerConformanceTests
{
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddTcpTest(listenBacklog);
}

/// <summary>Conformance tests for the tcp transport using IPv6.</summary>
[Parallelizable(ParallelScope.All)]
public class Ipv6TcpConnectionConformanceTests : DuplexConnectionConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp() => Ipv6SupportFixture.FixtureSetUp();

    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddTcpTest(listenBacklog, new Uri("icerpc://[::1]:0/"));
}

/// <summary>Conformance tests for the tcp transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class Ipv6TcpListenerConformanceTests : DuplexListenerConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp() => Ipv6SupportFixture.FixtureSetUp();

    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddTcpTest(listenBacklog, new Uri("icerpc://[::1]:0/"));
}

internal class Ipv6SupportFixture
{

    public static void FixtureSetUp()
    {
        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        }
        catch
        {
            Assert.Ignore("IPv6 is not supported on this platform");
        }
    }
}
