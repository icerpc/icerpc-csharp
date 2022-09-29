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

        var serverTransportOptions = new TcpServerTransportOptions
            {
                ListenerBackLog = 1
            };
        services.AddSingleton<IDuplexServerTransport>(provider => new TcpServerTransport(serverTransportOptions));

        services.AddSingleton<IDuplexClientTransport>(provider => new TcpClientTransport());

        return services;
    }
}
