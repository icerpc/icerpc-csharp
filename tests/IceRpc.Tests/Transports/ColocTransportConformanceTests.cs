// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the coloc transport.</summary>
[Parallelizable(ParallelScope.All)]
public class ColocTransportConformanceTests : DuplexTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        ColocTransportConformanceTestsServiceCollection.Create();
}

/// <summary>Conformance tests for the coloc transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class ColocListenerTransportConformanceTests : DuplexListenerTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        ColocTransportConformanceTestsServiceCollection.Create();
}

internal static class ColocTransportConformanceTestsServiceCollection
{
    internal static IServiceCollection Create() =>
        new ServiceCollection()
            .AddDuplexTransportClientServerTest(new Uri("icerpc://colochost/"))
            .AddColocTransport()
            .AddSingleton(_ => new ColocTransport(new ColocTransportOptions { ListenBacklog = 1 }));
}
