// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Slice;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Extension methods for setting up IceRpc.Slice services in an <see cref="IServiceCollection"/>.</summary>
public static class IceRpcSliceServiceCollectionExtensions
{
    /// <summary>Adds a Prx singleton to this service collection.</summary>
    /// <typeparam name="TPrx">The Prx interface generated by the Slice compiler.</typeparam>
    /// <typeparam name="TPrxImplementation">The implementation of TPrx.</typeparam>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="serviceAddressString">The service address of the proxy.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcPrx<TPrx, TPrxImplementation>(
        this IServiceCollection services,
        string serviceAddressString)
        where TPrx : class
        where TPrxImplementation : IProxy, TPrx, new() =>
        services
            .AddSingleton<TPrx>(provider =>
                new TPrxImplementation
                {
                    EncodeFeature = provider.GetService<ISliceEncodeFeature>(),
                    Invoker = provider.GetRequiredService<IInvoker>(),
                    ServiceAddress = ServiceAddress.Parse(serviceAddressString),
                });

    /// <summary>Adds a Prx singleton to this service collection, using the Prx interface default path and a protocol.
    /// </summary>
    /// <typeparam name="TPrx">The Prx interface generated by the Slice compiler.</typeparam>
    /// <typeparam name="TPrxImplementation">The implementation of TPrx.</typeparam>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="protocol">The protocol to use. Null is equivalent to IceRpc.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIceRpcPrx<TPrx, TPrxImplementation>(
        this IServiceCollection services,
        Protocol? protocol = null)
        where TPrx : class
        where TPrxImplementation : IProxy, TPrx, new() =>
        services.AddIceRpcPrx<TPrx, TPrxImplementation>(
            $"{protocol ?? Protocol.IceRpc}:{typeof(TPrx).GetDefaultPath()}");
}
