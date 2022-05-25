// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

/// <summary>Conformance tests for the coloc simple transport.</summary>
[Parallelizable(ParallelScope.All)]
public class ColocTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        new ServiceCollection().UseSimpleTransport().AddColocTransport().AddSingleton(
            typeof(Endpoint),
            new Endpoint(Protocol.IceRpc) { Host = "colochost"});
}
