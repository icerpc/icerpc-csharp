// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Tests.Transports.Tcp;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports.Slic;

/// <summary>Conformance tests for the Ssl duplex transport.</summary>
[Parallelizable(ParallelScope.All)]
public class SlicTransportSslAuthenticationConformanceTests : MultiplexedTransportSslAuthenticationConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        new ServiceCollection()
            .AddMultiplexedTransportTest(new Uri("icerpc://127.0.0.1:0/"))
            .AddSlicTransport()
            .AddTcpTransport();
}
