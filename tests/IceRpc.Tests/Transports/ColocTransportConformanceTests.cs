// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the coloc simple transport.</summary>
[Parallelizable(ParallelScope.All)]
public class ColocTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        new ServiceCollection().UseSimpleTransport("icerpc://colochost/").AddColocTransport();
}
