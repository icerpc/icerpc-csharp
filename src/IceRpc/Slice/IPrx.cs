// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>The common interface of all concrete typed proxies. It gives access to an untyped proxy object that can
    /// send requests to a remote IceRPC service.</summary>
    public interface IPrx
    {
        /// <summary>Gets the proxy object, or sets this proxy object during initialization.</summary>
        Proxy Proxy { get; init; }
    }

    /// <summary>Provides extension methods for typed proxies.</summary>
    public static class PrxExtensions
    {
        /// <summary>Tests whether the target service implements the interface implemented by the T typed proxy. This
        /// method is a wrapper for <see cref="IServicePrx.IceIsAAsync"/>.</summary>
        /// <paramtype name="T">The type of the desired typed proxy.</paramtype>
        /// <param name="prx">The source proxy being tested.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A new typed proxy with the desired type, or null.</returns>
        public static async Task<T?> AsAsync<T>(
            this IPrx prx,
            Invocation? invocation = null,
            CancellationToken cancel = default) where T : struct, IPrx =>
            await new ServicePrx(prx.Proxy).IceIsAAsync(typeof(T).GetIceTypeId()!, invocation, cancel).
                ConfigureAwait(false) ?
                new T { Proxy = prx.Proxy } : null;
    }

    /// <summary>Provides extension methods for IceDecoder.</summary>
    public static class IceDecoderPrxExtensions
    {
        /// <summary>Decodes a nullable typed proxy.</summary>
        /// <param name="decoder">The decoder.</param>
        /// <returns>The decoded proxy, or null.</returns>
        public static T? DecodeNullablePrx<T>(ref this IceDecoder decoder) where T : struct, IPrx =>
            decoder.DecodeNullableProxy() is Proxy proxy ? new T { Proxy = proxy } : null;
    }
}
