// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>Provides extension methods for proxies generated by the Slice compiler.</summary>
    public static class PrxExtensions
    {
        /// <summary>Tests whether the target service implements the interface implemented by the T typed proxy. This
        /// method is a wrapper for <see cref="IServicePrx.IceIsAAsync"/>.</summary>
        /// <paramtype name="TPrx">The type of the source Prx struct.</paramtype>
        /// <paramtype name="T">The type of the desired Prx struct.</paramtype>
        /// <param name="prx">The source Prx being tested.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A new typed proxy with the desired type, or null.</returns>
        public static async Task<T?> AsAsync<TPrx, T>(
            this TPrx prx,
            Invocation? invocation = null,
            CancellationToken cancel = default)
                where TPrx : struct, IPrx
                where T : struct, IPrx =>
            await new ServicePrx(prx.Proxy).IceIsAAsync(typeof(T).GetSliceTypeId()!, invocation, cancel).
                ConfigureAwait(false) ?
                new T { EncodeOptions = prx.EncodeOptions, Proxy = prx.Proxy } : null;

        /// <summary>Converts this Prx struct into a string using a specific format.</summary>
        /// <paramtype name="TPrx">The type of source Prx struct.</paramtype>
        /// <param name="prx">The Prx struct.</param>
        /// <param name="format">The proxy format.</param>
        public static string ToString<TPrx>(this TPrx prx, IProxyFormat format) where TPrx : struct, IPrx =>
            format.ToString(prx.Proxy);
    }
}
