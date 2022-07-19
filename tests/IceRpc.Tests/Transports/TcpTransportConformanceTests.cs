// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the tcp duplex transport.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpTransportConformanceTests : DuplexTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection().UseDuplexTransport(new Uri("icerpc://127.0.0.1:0/"));

        services.AddSingleton<IServerTransport<IDuplexConnection>>(provider => new TcpServerTransport());

        services.TryAddSingleton(new TcpClientTransportOptions());
        services.AddSingleton<IClientTransport<IDuplexConnection>>(provider => new TcpClientTransport());

        return services;
    }
}
