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
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddColocTest(listenBacklog);
}

/// <summary>Conformance tests for the coloc transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class ColocListenerConformanceTests : DuplexListenerConformanceTests
{
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddColocTest(listenBacklog);
}

internal static class ColocTransportServiceCollectionExtensions
{
    internal static IServiceCollection AddColocTest(this IServiceCollection serviceCollection, int? listenBacklog) =>
        serviceCollection
            .AddDuplexTransportTest(new Uri("icerpc://colochost/"))
            .AddColocTransport()
            .AddSingleton<ColocTransportOptions>(
                _ => listenBacklog is null ? new() : new() { ListenBacklog = listenBacklog.Value });
}
