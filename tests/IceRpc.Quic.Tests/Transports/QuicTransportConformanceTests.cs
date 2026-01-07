// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Quic;

namespace IceRpc.Tests.Transports;

[NonParallelizable]
public class QuicConnectionConformanceTests : MultiplexedConnectionConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("QUIC is not supported on this platform");
        }
    }

    /// <summary>Creates the service collection used for QUIC connection conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().AddQuicTest();
}

[NonParallelizable]
public class QuicStreamConformanceTests : MultiplexedStreamConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("QUIC is not supported on this platform");
        }
    }

    /// <summary>Creates the service collection used for QUIC stream conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().AddQuicTest();
}

[NonParallelizable]
public class QuicListenerConformanceTests : MultiplexedListenerConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("QUIC is not supported on this platform");
        }
    }

    /// <summary>Creates the service collection used for QUIC listener conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().AddQuicTest();
}
