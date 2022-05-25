// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

/// <summary>Conformance tests for the tcp simple transport.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        new ServiceCollection().UseSimpleTransport().UseTcp();
}
