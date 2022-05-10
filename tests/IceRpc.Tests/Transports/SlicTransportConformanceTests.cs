// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class SlicConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>Creates the service collection used for Slic multiplexed transports for conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() =>
        new ServiceCollection()
            .UseColoc()
            .UseSlic();
}
