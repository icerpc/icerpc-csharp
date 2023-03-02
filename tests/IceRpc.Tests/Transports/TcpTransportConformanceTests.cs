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

internal static class TcpTransportServiceCollectionExtensions
{
    public static IServiceCollection AddTcpTest(this IServiceCollection services, int? listenBacklog) => services
        .AddDuplexTransportTest(new Uri("icerpc://127.0.0.1:0/"))
        .AddTcpTransport()
        .AddSingleton<TcpServerTransportOptions>(
            _ => listenBacklog is null ? new() : new() { ListenBacklog = listenBacklog.Value });

    internal static IServiceCollection AddTcpTransport(this IServiceCollection serviceCollection) =>
        serviceCollection
            .AddSingleton<TcpClientTransportOptions>()
            .AddSingleton<TcpServerTransportOptions>()
            .AddSingleton<IDuplexServerTransport>(
                provider => new TcpServerTransport(provider.GetRequiredService<TcpServerTransportOptions>()))
            .AddSingleton<IDuplexClientTransport>(
                provider => new TcpClientTransport(provider.GetRequiredService<TcpClientTransportOptions>()));
}
