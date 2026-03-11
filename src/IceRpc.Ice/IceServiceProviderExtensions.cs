// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice;

/// <summary>Provides extension methods for <see cref="IServiceProvider" /> to create Ice proxies.</summary>
public static class IceServiceProviderExtensions
{
    /// <summary>Creates an Ice proxy with this service provider.</summary>
    /// <typeparam name="TProxy">The Ice proxy struct.</typeparam>
    /// <param name="provider">The service provider.</param>
    /// <param name="serviceAddress">The service address of the new proxy; null is equivalent to the default service
    /// address for the proxy type.</param>
    /// <returns>A new instance of <typeparamref name="TProxy" />.</returns>
    /// <remarks>The new proxy uses the <see cref="IInvoker" /> retrieved from <paramref name="provider" /> as its
    /// invocation pipeline, and the <see cref="IceEncodeOptions" /> retrieved from <paramref name="provider" /> as
    /// its encode options.</remarks>
    public static TProxy CreateIceProxy<TProxy>(this IServiceProvider provider, ServiceAddress? serviceAddress = null)
        where TProxy : struct, IIceProxy
    {
        var invoker = (IInvoker?)provider.GetService(typeof(IInvoker));
        if (invoker is null)
        {
            throw new InvalidOperationException("Could not find service of type 'IInvoker' in the service container.");
        }

        return serviceAddress is null ?
            new TProxy
            {
                EncodeOptions = (IceEncodeOptions?)provider.GetService(typeof(IceEncodeOptions)),
                Invoker = invoker
            }
            :
            new TProxy
            {
                EncodeOptions = (IceEncodeOptions?)provider.GetService(typeof(IceEncodeOptions)),
                Invoker = invoker,
                ServiceAddress = serviceAddress
            };
    }

    /// <summary>Creates an Ice proxy with this service provider.</summary>
    /// <typeparam name="TProxy">The Ice proxy struct.</typeparam>
    /// <param name="provider">The service provider.</param>
    /// <param name="serviceAddressUri">The service address of the proxy as a URI.</param>
    /// <returns>A new instance of <typeparamref name="TProxy" />.</returns>
    /// <remarks>The new proxy uses the <see cref="IInvoker" /> retrieved from <paramref name="provider" /> as its
    /// invocation pipeline, and the <see cref="IceEncodeOptions" /> retrieved from <paramref name="provider" /> as
    /// its encode options.</remarks>
    public static TProxy CreateIceProxy<TProxy>(this IServiceProvider provider, Uri serviceAddressUri)
        where TProxy : struct, IIceProxy =>
        provider.CreateIceProxy<TProxy>(new ServiceAddress(serviceAddressUri));
}
