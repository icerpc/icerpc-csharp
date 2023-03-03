// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

[Parallelizable(ParallelScope.All)]
public class QuicConnectionConformanceTests : MultiplexedConnectionConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
    }

    /// <summary>Creates the service collection used for Quic connection conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().AddQuicTest();
}

[Parallelizable(ParallelScope.All)]
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class QuicStreamConformanceTests : MultiplexedStreamConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
    }

    /// <summary>Creates the service collection used for Quic stream conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().AddQuicTest();
}

[Parallelizable(ParallelScope.All)]
public class QuicListenerConformanceTests : MultiplexedListenerConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
    }

    /// <summary>Creates the service collection used for Quic listener conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().AddQuicTest();
}
