// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Provides extension methods for <see cref="IServiceProvider" /> to create Slice proxies.</summary>
public static class SliceServiceProviderExtensions
{
    /// <summary>Creates a Slice proxy with this service provider.</summary>
    /// <typeparam name="TProxy">The Slice proxy struct.</typeparam>
    /// <param name="provider">The service provider.</param>
    /// <param name="serviceAddress">The service address of the new proxy; null is equivalent to the default service
    /// address for the proxy type.</param>
    /// <returns>A new instance of <typeparamref name="TProxy" />.</returns>
    /// <remarks>The new proxy uses the <see cref="IInvoker" /> retrieved from <paramref name="provider" /> as its
    /// invocation pipeline, and the <see cref="SliceEncodeOptions" /> retrieved from <paramref name="provider" /> as
    /// its encode options.</remarks>
    public static TProxy CreateSliceProxy<TProxy>(this IServiceProvider provider, ServiceAddress? serviceAddress = null)
        where TProxy : struct, IProxy =>
        serviceAddress is null ?
        new TProxy
        {
            EncodeOptions = (SliceEncodeOptions?)provider.GetService(typeof(SliceEncodeOptions)),
            Invoker = (IInvoker?)provider.GetService(typeof(IInvoker))
        }
        :
        new TProxy
        {
            EncodeOptions = (SliceEncodeOptions?)provider.GetService(typeof(SliceEncodeOptions)),
            Invoker = (IInvoker?)provider.GetService(typeof(IInvoker)),
            ServiceAddress = serviceAddress
        };

    /// <summary>Creates a Slice proxy with this service provider.</summary>
    /// <typeparam name="TProxy">The Slice proxy struct.</typeparam>
    /// <param name="provider">The service provider.</param>
    /// <param name="serviceAddressUri">The service address of the proxy as a URI.</param>
    /// <returns>A new instance of <typeparamref name="TProxy" />.</returns>
    /// <remarks>The new proxy uses the <see cref="IInvoker" /> retrieved from <paramref name="provider" /> as its
    /// invocation pipeline, and the <see cref="SliceEncodeOptions" /> retrieved from <paramref name="provider" /> as
    /// its encode options.</remarks>
    public static TProxy CreateSliceProxy<TProxy>(this IServiceProvider provider, Uri serviceAddressUri)
        where TProxy : struct, IProxy =>
        provider.CreateSliceProxy<TProxy>(new ServiceAddress(serviceAddressUri));
}
