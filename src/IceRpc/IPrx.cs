// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The common interface of all concrete typed proxies. It gives access to an untyped proxy object that
    /// can send requests to a remote IceRPC service.</summary>
    public interface IPrx
    {
        /// <summary>Gets the proxy object, or sets this proxy object during initialization.</summary>
        Proxy Proxy { get; init; }
    }

    public static class PrxExtensions
    {
        /// <summary>Creates a new typed proxy from this typed proxy.</summary>
        /// <paramtype name="T">The type of the new proxy.</paramtype>
        /// <param name="prx">The source typed proxy.</param>
        /// <returns>A proxy with the specified type.</returns>
        public static T As<T>(this IPrx prx) where T : IPrx, new() => new T { Proxy = prx.Proxy };

        /// <summary>Tests whether the target service implements the interface implemented by the T typed proxy. This
        /// method is a wrapper for <see cref="IServicePrx.IceIdAAsync"/>.</summary>
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
        }
}
