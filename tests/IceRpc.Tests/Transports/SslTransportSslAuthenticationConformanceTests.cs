// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the Ssl duplex transport.</summary>
[Parallelizable(ParallelScope.All)]
public class SslTransportSslAuthenticationConformanceTests : DuplexTransportSslAuthenticationConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection()
        .AddDuplexTransportTest(new Uri("icerpc://127.0.0.1:0/"))
        .AddTcpTransport();
}
