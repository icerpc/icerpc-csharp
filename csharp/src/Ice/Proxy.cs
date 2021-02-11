// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    /// <summary>Proxy provides extension methods for IObjectPrx.</summary>
    public static class Proxy
    {
        /// <summary>Tests whether this proxy points to a remote object derived from T. If so it returns a proxy of
        /// type T otherwise returns null. This is a convenience wrapper for <see cref="IObjectPrx.IceIsAAsync"/>.
        /// </summary>
        /// <param name="proxy">The source proxy.</param>
        /// <param name="factory">The proxy factory used to specify the desired proxy type.</param>
        /// <param name="context">The context dictionary for the invocation.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A new proxy manufactured by the proxy factory, or null.</returns>
        public static async Task<T?> CheckedCastAsync<T>(
            this IObjectPrx proxy,
            T factory,
            IReadOnlyDictionary<string, string>? context = null,
            IProgress<bool>? progress = null,
            CancellationToken cancel = default) where T : class, IObjectPrx =>
            await proxy.IceIsAAsync(typeof(T).GetIceTypeId()!, context, progress, cancel).ConfigureAwait(false) ?
                (proxy is T t ? t : Clone(proxy, factory)) : null;

        /// <summary>Creates a clone of this proxy, with a new identity and optionally other options. The clone
        /// is identical to this proxy except for its identity and other options set through parameters.</summary>
        /// <param name="proxy">The source proxy.</param>
        /// <param name="factory">The proxy factory used to specify the desired proxy type.</param>
        /// <param name="cacheConnection">Determines whether or not the clone caches its connection (optional).</param>
        /// <param name="clearLabel">When set to true, the clone does not have an associated label (optional).</param>
        /// <param name="clearLocator">When set to true, the clone does not have an associated locator proxy (optional).
        /// </param>
        /// <param name="context">The context of the clone (optional).</param>
        /// <param name="encoding">The encoding of the clone (optional).</param>
        /// <param name="endpoints">The endpoints of the clone (optional).</param>
        /// <param name="facet">The facet of the clone (optional).</param>
        /// <param name="fixedConnection">The connection of the clone (optional). When specified, the clone is a fixed
        /// proxy. You can clone a non-fixed proxy into a fixed proxy but not vice-versa.</param>
        /// <param name="identity">The identity of the clone.</param>
        /// <param name="identityAndFacet">A relative URI string [category/]identity[#facet].</param>
        /// <param name="invocationInterceptors">A collection of <see cref="InvocationInterceptor"/> that will be
        /// executed with each invocation</param>
        /// <param name="invocationTimeout">The invocation timeout of the clone (optional).</param>
        /// <param name="label">The label of the clone (optional).</param>
        /// <param name="location">The location of the clone (optional).</param>
        /// <param name="locator">The locator proxy of the clone (optional).</param>
        /// <param name="locatorCacheTimeout">The locator cache timeout of the clone (optional).</param>
        /// <param name="oneway">Determines whether the clone is oneway or twoway (optional).</param>
        /// <param name="preferExistingConnection">Determines whether or not the clone prefer using an existing
        /// connection.</param>
        /// <param name="preferNonSecure">Determines whether the clone prefers non-secure connections over secure
        /// connections (optional).</param>
        /// <param name="relative">When true, the new proxy is a relative proxy (optional).</param>
        /// <returns>A new proxy manufactured by the proxy factory (see factory parameter).</returns>
        public static T Clone<T>(
            this IObjectPrx proxy,
            T factory,
            bool? cacheConnection = null,
            bool clearLabel = false,
            bool clearLocator = false,
            IReadOnlyDictionary<string, string>? context = null,
            Encoding? encoding = null,
            IEnumerable<Endpoint>? endpoints = null,
            string? facet = null,
            Connection? fixedConnection = null,
            Identity? identity = null,
            string? identityAndFacet = null,
            IEnumerable<InvocationInterceptor>? invocationInterceptors = null,
            TimeSpan? invocationTimeout = null,
            object? label = null,
            IEnumerable<string>? location = null,
            ILocatorPrx? locator = null,
            TimeSpan? locatorCacheTimeout = null,
            bool? oneway = null,
            bool? preferExistingConnection = null,
            NonSecure? preferNonSecure = null,
            bool? relative = null) where T : class, IObjectPrx =>
            ObjectPrx.Clone(proxy.Impl,
                            factory,
                            cacheConnection,
                            clearLabel,
                            clearLocator,
                            context,
                            encoding,
                            endpoints,
                            facet,
                            fixedConnection,
                            identity,
                            identityAndFacet,
                            invocationInterceptors,
                            invocationTimeout,
                            label,
                            location,
                            locator,
                            locatorCacheTimeout,
                            oneway,
                            preferExistingConnection,
                            preferNonSecure,
                            relative);

        /// <summary>Creates a clone of this proxy. The clone is identical to this proxy except for options set
        /// through parameters. This method returns this proxy instead of a new proxy in the event none of the options
        /// specified through the parameters change this proxy's options.</summary>
        /// <param name="proxy">The source proxy.</param>
        /// <param name="cacheConnection">Determines whether or not the clone caches its connection (optional).</param>
        /// <param name="clearLabel">When set to true, the clone does not have an associated label (optional).</param>
        /// <param name="clearLocator">When set to true, the clone does not have an associated locator proxy (optional).
        /// </param>
        /// <param name="context">The context of the clone (optional).</param>
        /// <param name="encoding">The encoding of the clone (optional).</param>
        /// <param name="endpoints">The endpoints of the clone (optional).</param>
        /// <param name="fixedConnection">The connection of the clone (optional). When specified, the clone is a fixed
        /// proxy. You can clone a non-fixed proxy into a fixed proxy but not vice-versa.</param>
        /// <param name="invocationInterceptors">A collection of <see cref="InvocationInterceptor"/> that will be
        /// executed with each invocation</param>
        /// <param name="invocationTimeout">The invocation timeout of the clone (optional).</param>
        /// <param name="label">The label of the clone (optional).</param>
        /// <param name="location">The location of the clone (optional).</param>
        /// <param name="locator">The locator proxy of the clone (optional).</param>
        /// <param name="locatorCacheTimeout">The locator cache timeout of the clone (optional).</param>
        /// <param name="oneway">Determines whether the clone is oneway or twoway (optional).</param>
        /// <param name="preferExistingConnection">Determines whether or not the clone prefer using an existing
        /// connection.</param>
        /// <param name="preferNonSecure">Determines whether the clone prefers non-secure connections over secure
        /// connections (optional).</param>
        /// <param name="relative">When true, the new proxy is a relative proxy (optional).</param>
        /// <returns>A new proxy with the same type as this proxy.</returns>
        public static T Clone<T>(
            this T proxy,
            bool? cacheConnection = null,
            bool clearLabel = false,
            bool clearLocator = false,
            IReadOnlyDictionary<string, string>? context = null,
            Encoding? encoding = null,
            IEnumerable<Endpoint>? endpoints = null,
            Connection? fixedConnection = null,
            IEnumerable<InvocationInterceptor>? invocationInterceptors = null,
            TimeSpan? invocationTimeout = null,
            object? label = null,
            IEnumerable<string>? location = null,
            ILocatorPrx? locator = null,
            TimeSpan? locatorCacheTimeout = null,
            bool? oneway = null,
            bool? preferExistingConnection = null,
            NonSecure? preferNonSecure = null,
            bool? relative = null) where T : class, IObjectPrx =>
            ObjectPrx.Clone(proxy.Impl,
                            proxy,
                            cacheConnection,
                            clearLabel,
                            clearLocator,
                            context,
                            encoding,
                            endpoints,
                            facet: null,
                            fixedConnection,
                            identity: null,
                            identityAndFacet: null,
                            invocationInterceptors,
                            invocationTimeout,
                            label,
                            location,
                            locator,
                            locatorCacheTimeout,
                            oneway,
                            preferExistingConnection,
                            preferNonSecure,
                            relative);

        /// <summary>Forwards an incoming request to another Ice object represented by the <paramref name="proxy"/>
        /// parameter.</summary>
        /// <remarks>When the incoming request frame's protocol and proxy's protocol are different, this method
        /// automatically bridges between these two protocols. When proxy's protocol is ice1, the resulting outgoing
        /// request frame is never compressed.</remarks>
        /// <param name="proxy">The proxy for the target Ice object.</param>
        /// <param name="request">The incoming request frame to forward to proxy's target.</param>
        /// <param name="oneway">When true, the request is sent as a oneway request. When false, it is sent as a
        /// two-way request.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A task holding the response frame.</returns>
        public static async ValueTask<OutgoingResponseFrame> ForwardAsync(
            this IObjectPrx proxy,
            IncomingRequestFrame request,
            bool oneway,
            IProgress<bool>? progress = null,
            CancellationToken cancel = default)
        {
            var forwardedRequest = new OutgoingRequestFrame(proxy, request, cancel: cancel);
            try
            {
                // TODO: add support for stream data forwarding.
                using IncomingResponseFrame response =
                    await ObjectPrx.InvokeAsync(proxy, forwardedRequest, oneway, progress).ConfigureAwait(false);
                return new OutgoingResponseFrame(request, response);
            }
            catch (LimitExceededException exception)
            {
                return new OutgoingResponseFrame(request, new ServerException(exception.Message, exception));
            }
        }

        /// <summary>Returns the cached Connection for this proxy. If the proxy does not yet have an established
        /// connection, it does not attempt to create a connection.</summary>
        /// <param name="proxy">The proxy.</param>
        /// <returns>The cached Connection for this proxy (null if the proxy does not have
        /// an established connection).</returns>
        public static Connection? GetCachedConnection(this IObjectPrx proxy) =>
            proxy.Impl.GetCachedConnection();

        /// <summary>Returns the Connection for this proxy. If the proxy does not yet have an established connection,
        /// it first attempts to create a connection.</summary>
        /// <param name="proxy">The proxy.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The Connection for this proxy.</returns>
        public static ValueTask<Connection> GetConnectionAsync(
            this IObjectPrx proxy,
            CancellationToken cancel = default) =>
            proxy.Impl.GetConnectionAsync(cancel);

        /// <summary>Invokes a request on a proxy.</summary>
        /// <remarks>request.CancellationToken holds the cancellation token.</remarks>
        /// <param name="proxy">The proxy for the target Ice object.</param>
        /// <param name="request">The request frame.</param>
        /// <param name="oneway">When true, the request is sent as a oneway request. When false, it is sent as a
        /// two-way request.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <returns>A task holding the response frame.</returns>
        public static Task<IncomingResponseFrame> InvokeAsync(
            this IObjectPrx proxy,
            OutgoingRequestFrame request,
            bool oneway = false,
            IProgress<bool>? progress = null) =>
            ObjectPrx.InvokeAsync(proxy, request, oneway, progress);

        /// <summary>Produces a string representation of a location.</summary>
        /// <param name="location">The location.</param>
        /// <returns>The location as a percent-escaped string with segments separated by '/'.</returns>
        public static string ToLocationString(this IEnumerable<string> location) =>
            string.Join('/', location.Select(s => Uri.EscapeDataString(s)));

        /// <summary>Converts a proxy to a set of proxy properties.</summary>
        /// <param name="proxy">The proxy for the target Ice object.</param>
        /// <param name="property">The base property name.</param>
        /// <returns>The property set.</returns>
        public static Dictionary<string, string> ToProperty(this IObjectPrx proxy, string property) =>
            proxy.Impl.ToProperty(property);
    }
}
