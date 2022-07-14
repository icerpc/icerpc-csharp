// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the coloc single stream transport.</summary>
[Parallelizable(ParallelScope.All)]
public class ColocTransportConformanceTests : SingleStreamTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        new ServiceCollection().UseSingleStreamTransport("icerpc://colochost/").AddColocTransport();
}
