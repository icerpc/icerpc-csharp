// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports.Tcp;

/// <summary>Conformance tests for the SSL transport.</summary>
[Parallelizable(ParallelScope.All)]
public class SslConnectionConformanceTests : TcpConnectionConformanceTests
{
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddSslTest(listenBacklog);
}

/// <summary>Conformance tests for the SSL transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class SslListenerConformanceTests : TcpListenerConformanceTests
{
    protected override IServiceCollection CreateServiceCollection(int? listenBacklog) =>
        new ServiceCollection().AddSslTest(listenBacklog);
}
