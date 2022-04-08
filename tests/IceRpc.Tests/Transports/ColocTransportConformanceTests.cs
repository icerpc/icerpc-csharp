// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

public class ColocTransportServiceCollection : SimpleTransportServiceCollection
{
    public ColocTransportServiceCollection()
    {
        var coloc = new ColocTransport();
        this.AddScoped(_ => coloc.ServerTransport);
        this.AddScoped(_ => coloc.ClientTransport);
        this.AddScoped(typeof(Endpoint), provider => Endpoint.FromString($"icerpc://{Guid.NewGuid()}/"));
    }
}

/// <summary>Conformance tests for the coloc simple transport.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class ColocTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override ServiceCollection CreateServiceCollection() =>
        new ColocTransportServiceCollection();
}
