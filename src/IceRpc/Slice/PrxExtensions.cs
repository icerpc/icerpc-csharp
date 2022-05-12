
// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using System.IO.Pipelines;

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


        /// <summary>Creates a payload stream from an async enumerable.</summary>
        /// <paramtype name="TPrx">The type of source Prx struct.</paramtype>
        /// <param name="prx">The Prx struct.</param>
        /// <param name="asyncEnumerable">The async enumerable to encode and stream.</param>
        /// <param name="encoding">The encoding of the payload.</param>
        /// <param name="encodeAction">The action used to encode the streamed member.</param>
        /// <param name="useSegments"><c>true</c> if we are encoding a stream elements in segments this is the case
        /// when the streamed elements are of variable size; otherwise, <c>false</c>.</param>
        public static PipeReader CreatePayloadStream<TPrx, T>(
            this TPrx prx,
            IAsyncEnumerable<T> asyncEnumerable,
            SliceEncoding encoding,
            EncodeAction<T> encodeAction,
            bool useSegments) where TPrx : struct, IPrx =>
            new SliceEncodingExtensions.PayloadStreamPipeReader<T>(
                encoding,
                asyncEnumerable,
                prx.EncodeOptions,
                encodeAction,
                useSegments);

        /// <summary>Converts this Prx struct into a string using a specific format.</summary>
        /// <paramtype name="TPrx">The type of source Prx struct.</paramtype>
        /// <param name="prx">The Prx struct.</param>
        /// <param name="format">The proxy format.</param>
        public static string ToString<TPrx>(this TPrx prx, IProxyFormat format) where TPrx : struct, IPrx =>
            format.ToString(prx.Proxy);
    }
}
