// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

public static class ColocTransportServiceCollectionExtensions
{
    public static ServiceCollection UseColoc(this ServiceCollection serviceCollection)
    {
        var coloc = new ColocTransport();
        serviceCollection.AddScoped(_ => coloc.ServerTransport);
        serviceCollection.AddScoped(_ => coloc.ClientTransport);
        serviceCollection.AddScoped(typeof(Endpoint), provider => Endpoint.FromString($"icerpc://{Guid.NewGuid()}/"));
        return serviceCollection;
    }
}

/// <summary>Conformance tests for the coloc simple transport.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class ColocTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override ServiceCollection CreateServiceCollection() =>
        new ServiceCollection().UseSimpleTransport().UseColoc();
}
