// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

public class TcpTransportServiceCollection : SimpleTransportServiceCollection
{
    public TcpTransportServiceCollection()
    {
        this.AddScoped<IServerTransport<ISimpleNetworkConnection>>(_ => new TcpServerTransport());
        this.AddScoped<IClientTransport<ISimpleNetworkConnection>>(_ => new TcpClientTransport());
        this.AddScoped(typeof(Endpoint), provider => Endpoint.FromString("icerpc://127.0.0.1:0/"));
    }
}

/// <summary>Conformance tests for the tcp simple transport.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class TcpTransportConformanceTests : SimpleTransportConformanceTests
{
    public override ServiceCollection CreateServiceCollection() =>
        new TcpTransportServiceCollection();
}
