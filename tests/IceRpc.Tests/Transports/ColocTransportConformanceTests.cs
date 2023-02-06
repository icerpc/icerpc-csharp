// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the coloc transport.</summary>
[Parallelizable(ParallelScope.All)]
public class ColocConnectionConformanceTests : DuplexConnectionConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseColoc();
}

/// <summary>Conformance tests for the coloc transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class ColocListenerConformanceTests : DuplexListenerConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseColoc();
}

internal static class ColocTransportConformanceTestsServiceCollection
{
    internal static IServiceCollection UseColoc(this IServiceCollection serviceCollection) =>
        serviceCollection
            .AddDuplexTransportClientServerTest(new Uri("icerpc://colochost/"))
            .AddColocTransport()
            .AddSingleton(_ => new ColocTransport(new ColocTransportOptions { ListenBacklog = 1 }));
}
