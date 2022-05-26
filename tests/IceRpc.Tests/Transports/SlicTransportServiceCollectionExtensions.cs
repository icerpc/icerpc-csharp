// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IceRpc.Transports.Tests;

public static class SlicTransportServiceCollectionExtensions
{
    public static IServiceCollection AddSlicTest(this IServiceCollection services, SlicTransportOptions slicTransportOptions)
    {
        services.AddColocTransport();
        services.AddSingleton(typeof(Endpoint), new Endpoint(Protocol.IceRpc) { Host = "colochost" });
        services.AddOptions<SlicTransportOptions>().Configure(
            options =>
            {
                options.BidirectionalStreamMaxCount = slicTransportOptions.BidirectionalStreamMaxCount;
                options.PacketMaxSize = slicTransportOptions.PacketMaxSize;
                options.PauseWriterThreshold = slicTransportOptions.PauseWriterThreshold;
                options.Pool = slicTransportOptions.Pool;
                options.ResumeWriterThreshold = slicTransportOptions.ResumeWriterThreshold;
            });
        services.
            TryAddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

        services.
            TryAddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));
        services.AddSingleton(provider =>
        {
            var serverTransport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
            var listener = serverTransport.Listen(
                (Endpoint)provider.GetRequiredService(typeof(Endpoint)),
                null,
                NullLogger.Instance);
            return listener;
        });
        return services;
    }
}
