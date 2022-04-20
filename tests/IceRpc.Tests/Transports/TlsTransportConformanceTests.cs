// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

/// <summary>Conformance tests for the tls simple transport.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class TlsTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override ServiceCollection CreateServiceCollection() => new TcpTransportServiceCollection().UseSsl();
}
