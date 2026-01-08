// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IceRpc.Tests.Transports.Slic;

internal static class SlicTransportServiceCollectionExtensions
{
    internal static IServiceCollection AddSlicTest(this IServiceCollection services) =>
        services.AddMultiplexedTransportTest().AddColocTransport().AddSlicTransport();
}
