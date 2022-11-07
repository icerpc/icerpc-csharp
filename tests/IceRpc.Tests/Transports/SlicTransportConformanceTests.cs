// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net.Security;

namespace IceRpc.Tests.Transports;

[Parallelizable(ParallelScope.All)]
public class SlicTransportConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>Creates the service collection used for Slic multiplexed transport conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection()
    {
        IServiceCollection services = new ServiceCollection()
            .AddColocTransport()
            .AddSingleton<IMultiplexedServerTransport>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("server"),
                    provider.GetRequiredService<IDuplexServerTransport>()))
            .AddSingleton<IMultiplexedClientTransport>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("client"),
                    provider.GetRequiredService<IDuplexClientTransport>()))
            .AddSingleton(provider =>
                provider.GetRequiredService<IMultiplexedServerTransport>().Listen(
                    new ServerAddress(Protocol.IceRpc) { Host = "colochost" },
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    serverAuthenticationOptions: provider.GetService<SslServerAuthenticationOptions>()));

        services.AddOptions<SlicTransportOptions>("client");
        services.AddOptions<SlicTransportOptions>("server");
        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.PayloadErrorCodeConverter = IceRpcProtocol.Instance.PayloadErrorCodeConverter);

        return services;
    }
}
