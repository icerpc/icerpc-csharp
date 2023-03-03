// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the tcp transport.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpConnectionConformanceTests : DuplexConnectionConformanceTests
{
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddTcpTest(listenBacklog);
}

/// <summary>Conformance tests for the tcp transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpListenerConformanceTests : DuplexListenerConformanceTests
{
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddTcpTest(listenBacklog);
}
