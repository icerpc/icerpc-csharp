// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports.Coloc;

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
