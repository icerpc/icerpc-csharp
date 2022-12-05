// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the tcp duplex transport.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpTransportConformanceTests : DuplexTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection()
        .AddDuplexTransportClientServerTest(new Uri("icerpc://127.0.0.1:0/"))
        .AddSingleton<IDuplexServerTransport>(provider => new TcpServerTransport(new TcpServerTransportOptions
        {
            ListenBacklog = 1
        }))
        .AddSingleton<IDuplexClientTransport>(provider => new TcpClientTransport());
}
