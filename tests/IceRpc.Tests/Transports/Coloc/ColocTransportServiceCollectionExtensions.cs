// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports.Coloc;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Tests.Transports.Coloc;

internal static class ColocTransportServiceCollectionExtensions
{
    internal static IServiceCollection AddColocTest(this IServiceCollection serviceCollection, int? listenBacklog) =>
        serviceCollection
            .AddDuplexTransportTest(new Uri("icerpc://colochost/"))
            .AddColocTransport()
            .AddSingleton<ColocTransportOptions>(
                _ => listenBacklog is null ? new() : new() { ListenBacklog = listenBacklog.Value });
}
