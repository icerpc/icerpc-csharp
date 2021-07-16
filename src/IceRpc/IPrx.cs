// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The common interface of all concrete typed proxies. It gives access to an untyped proxy object that can
    /// send requests to a remote IceRPC service.</summary>
    public interface IPrx
    {
        /// <summary>Gets the proxy object, or sets this proxy object during initialization.</summary>
        Proxy Proxy { get; init; }
    }

    /// <summary>Provides encode actions for typed proxies.</summary>
    public static class PrxEncodeActions
    {
        /// <summary>An <see cref="EncodeAction{T}"/> for <see cref="IPrx"/>.</summary>
        public static readonly EncodeAction<IPrx> PrxEncodeAction =
            (encoder, prx) => encoder.EncodeProxy(prx.Proxy);

        /// <summary>An <see cref="EncodeAction{T}"/> for a nullable <see cref="IPrx"/>.</summary>
        public static readonly EncodeAction<IPrx?> NullablePrxEncodeAction =
            (encoder, prx) => encoder.EncodeNullableProxy(prx?.Proxy);
    }

    /// <summary>Provides extension methods for typed proxies.</summary>
    public static class PrxExtensions
    {
        /// <summary>Creates a new typed proxy from this typed proxy.</summary>
        /// <paramtype name="T">The type of the new proxy.</paramtype>
        /// <param name="prx">The source typed proxy.</param>
        /// <returns>A proxy with the specified type.</returns>
        public static T As<T>(this IPrx prx) where T : IPrx, new() => new T { Proxy = prx.Proxy };

        /// <summary>Tests whether the target service implements the interface implemented by the T typed proxy. This
        /// method is a wrapper for <see cref="IServicePrx.IceIsAAsync"/>.</summary>
        /// <paramtype name="T">The type of the desired typed proxy.</paramtype>
        /// <param name="prx">The source proxy being tested.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A new typed proxy with the desired type, or null.</returns>
        public static async Task<T?> CheckedCastAsync<T>(
            this IPrx prx,
            Invocation? invocation = null,
            CancellationToken cancel = default) where T : struct, IPrx =>
            await new ServicePrx(prx.Proxy).IceIsAAsync(typeof(T).GetIceTypeId()!, invocation, cancel).
                ConfigureAwait(false) ?
                new T { Proxy = prx.Proxy } : null;

        /// <summary>Creates a copy of this proxy with a new path and type.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="prx">The proxy being copied.</param>
        /// <param name="path">The new path.</param>
        /// <returns>A proxy with the specified path and type.</returns>
        public static T WithPath<T>(this IPrx prx, string path) where T : IPrx, new()
        {
            Proxy source = prx.Proxy;

            if (path == source.Path && prx is T newPrx)
            {
                return newPrx;
            }

            var newProxy = Proxy.FromPath(path, source.Protocol);
            if (newProxy.Protocol == Protocol.Ice1)
            {
                newProxy.Facet = source.GetFacet();
                // clear cached connection of well-known proxy
                newProxy.Connection = source.Endpoint == null ? null : source.Connection;
            }
            else
            {
                newProxy.Connection = source.Connection;
            }

            newProxy.AltEndpoints = source.AltEndpoints;
            newProxy.Encoding = source.Encoding;
            newProxy.Endpoint = source.Endpoint;
            newProxy.Invoker = source.Invoker;
            return new T { Proxy = newProxy };
        }
    }

    /// <summary>Provides extension methods for IceDecoder.</summary>
    public static class IceDecoderPrxExtensions
    {
        /// <summary>Decodes a nullable typed proxy.</summary>
        /// <param name="decoder">The decoder.</param>
        /// <returns>The decoded proxy, or null.</returns>
        public static T? DecodeNullablePrx<T>(this IceDecoder decoder) where T : struct, IPrx =>
            decoder.DecodeNullableProxy() is Proxy proxy ? new T { Proxy = proxy } : null;

        /// <summary>Decodes a tagged typed proxy.</summary>
        /// <param name="decoder">The decoder.</param>
        /// <param name="tag">The tag.</param>
        /// <returns>The decoded proxy, or null.</returns>
        public static T? DecodeTaggedPrx<T>(this IceDecoder decoder, int tag) where T : struct, IPrx =>
            decoder.DecodeTaggedProxy(tag) is Proxy proxy ? new T { Proxy = proxy } : null;
    }
}
