// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the tcp simple transport.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection().UseSimpleTransport("icerpc://127.0.0.1:0/");

        services.AddSingleton<IServerTransport<ISimpleNetworkConnection>>(provider => new TcpServerTransport());

        services.TryAddSingleton(new TcpClientTransportOptions());
        services.AddSingleton<IClientTransport<ISimpleNetworkConnection>>(provider => new TcpClientTransport());

        return services;
    }
}
