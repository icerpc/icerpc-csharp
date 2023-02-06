// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the tcp transport.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpConnectionConformanceTests : DuplexConnectionConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseTcp();
}

/// <summary>Conformance tests for the tcp transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpListenerConformanceTests : DuplexListenerConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseTcp();
}

internal static class TcpTransportConformanceTestsServiceCollection
{
    internal static IServiceCollection UseTcp(this IServiceCollection serviceCollection) =>
        serviceCollection
            .AddDuplexTransportClientServerTest(new Uri("icerpc://127.0.0.1:0/"))
            .AddSingleton<IDuplexServerTransport>(provider => new TcpServerTransport(new TcpServerTransportOptions
            {
                ListenBacklog = 1
            }))
            .AddSingleton<IDuplexClientTransport>(provider => new TcpClientTransport());
}
