// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Tests.Transports.Slic;

internal static class SlicTransportServiceCollectionExtensions
{
    internal static IServiceCollection AddSlicTest(this IServiceCollection services) =>
        services.AddMultiplexedTransportTest().AddColocTransport().AddSlicTransport();
}
