// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

[Parallelizable(ParallelScope.All)]
public class SlicConnectionConformanceTests : MultiplexedConnectionConformanceTests
{
    /// <summary>Creates the service collection used for Slic connection conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().AddSlicTest();
}

[Parallelizable(ParallelScope.All)]
public class SlicStreamConformanceTests : MultiplexedStreamConformanceTests
{
    /// <summary>Creates the service collection used for Slic stream conformance testing.
    /// </summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().AddSlicTest();
}

[Parallelizable(ParallelScope.All)]
public class SlicListenerConformanceTests : MultiplexedListenerConformanceTests
{
    /// <summary>Creates the service collection used for Slic listener conformance testing.
    /// </summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().AddSlicTest();
}
