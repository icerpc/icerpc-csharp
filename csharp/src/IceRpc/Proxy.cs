// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Proxy provides extension methods for IServicePrx.</summary>
    public static class Proxy
    {
        /// <summary>Creates a clone of this proxy. The clone is identical to this proxy except for the options set
        /// through parameters. This method returns this proxy instead of a new proxy in the event none of the options
        /// specified through the parameters change anything.</summary>
        /// <param name="proxy">The source proxy.</param>
        /// <param name="cacheConnection">Determines whether or not the clone caches its connection (optional).</param>
        /// <param name="clearLabel">When set to true, the clone does not have an associated label (optional).</param>
        /// <param name="clearLocationService">When set to true, the clone does not have an associated location service
        /// (optional).</param>
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
        /// <param name="locationService">The location service of the clone (optional).</param>
        /// <param name="oneway">Determines whether the clone is oneway or twoway (optional).</param>
        /// <param name="preferExistingConnection">Determines whether or not the clone prefer using an existing
        /// connection.</param>
        /// <param name="preferNonSecure">Determines whether the clone prefers non-secure connections over secure
        /// connections (optional).</param>
        /// <returns>A new proxy with the same type as this proxy.</returns>
        public static T Clone<T>(
            this T proxy,
            bool? cacheConnection = null,
            bool clearLabel = false,
            bool clearLocationService = false,
            IReadOnlyDictionary<string, string>? context = null,
            Encoding? encoding = null,
            IEnumerable<Endpoint>? endpoints = null,
            Connection? fixedConnection = null,
            IEnumerable<InvocationInterceptor>? invocationInterceptors = null,
            TimeSpan? invocationTimeout = null,
            object? label = null,
            string? location = null,
            ILocationService? locationService = null,
            bool? oneway = null,
            bool? preferExistingConnection = null,
            NonSecure? preferNonSecure = null) where T : class, IServicePrx
        {
            ServicePrx impl = proxy.Impl;
            ServicePrx clone = impl.Clone(impl.CreateCloneOptions(cacheConnection,
                                                                 clearLabel,
                                                                 clearLocationService,
                                                                 context,
                                                                 encoding,
                                                                 endpoints,
                                                                 facet: null,
                                                                 fixedConnection,
                                                                 invocationInterceptors,
                                                                 invocationTimeout,
                                                                 label,
                                                                 location,
                                                                 locationService,
                                                                 oneway,
                                                                 path: null,
                                                                 preferExistingConnection,
                                                                 preferNonSecure));
            return clone == impl ? proxy : (clone as T)!;
        }

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
            this IServicePrx proxy,
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
                    await ServicePrx.InvokeAsync(proxy, forwardedRequest, oneway, progress).ConfigureAwait(false);
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
        public static Connection? GetCachedConnection(this IServicePrx proxy) =>
            proxy.Impl.GetCachedConnection();

        /// <summary>Returns the Connection for this proxy. If the proxy does not yet have an established connection,
        /// it first attempts to create a connection.</summary>
        /// <param name="proxy">The proxy.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The Connection for this proxy.</returns>
        public static ValueTask<Connection> GetConnectionAsync(
            this IServicePrx proxy,
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
            this IServicePrx proxy,
            OutgoingRequestFrame request,
            bool oneway = false,
            IProgress<bool>? progress = null) =>
            ServicePrx.InvokeAsync(proxy, request, oneway, progress);

        /// <summary>Converts a proxy to a set of proxy properties.</summary>
        /// <param name="proxy">The proxy for the target Ice object.</param>
        /// <param name="property">The base property name.</param>
        /// <returns>The property set.</returns>
        public static Dictionary<string, string> ToProperty(this IServicePrx proxy, string property) =>
            proxy.Impl.ToProperty(property);
    }
}
