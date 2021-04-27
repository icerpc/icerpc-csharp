// Copyright (c) ZeroC, Inc. All rights reserved.
using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>Proxy provides extension methods for IServicePrx.</summary>
    public static class Proxy
    {
        /// <summary>Creates a copy of this proxy with a new proxy type.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="proxy">The proxy being copied.</param>
        /// <returns>A proxy with the desired type.</returns>
        public static T As<T>(this IServicePrx proxy) where T : class, IServicePrx
        {
            if (proxy.Protocol == Protocol.Ice1)
            {
                return GetFactory<T>().Create(proxy.GetIdentity(),
                                              proxy.GetFacet(),
                                              proxy.Encoding,
                                              proxy.Impl.ParsedEndpoint,
                                              proxy.Impl.ParsedAltEndpoints,
                                              proxy.Connection,
                                              proxy.GetOptions());
            }
            else
            {
                return GetFactory<T>().Create(proxy.Path,
                                              proxy.Protocol,
                                              proxy.Encoding,
                                              proxy.Impl.ParsedEndpoint,
                                              proxy.Impl.ParsedAltEndpoints,
                                              proxy.Connection,
                                              proxy.GetOptions());
            }
        }

        /// <summary>Tests whether a proxy points to a remote service whose associated proxy interface is T or an
        /// interface type derived from T. If so, returns a proxy of type, otherwise returns null. This is a convenience
        /// wrapper for <see cref="IServicePrx.IceIsAAsync"/>.
        /// </summary>
        /// <paramtype name="T">The type of the desired service proxy.</paramtype>
        /// <param name="proxy">The source proxy being tested.</param>
        /// <param name="context">The context dictionary for the invocation.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A new proxy with the desired type, or null.</returns>
        public static async Task<T?> CheckedCastAsync<T>(
            this IServicePrx proxy,
            IReadOnlyDictionary<string, string>? context = null,
            IProgress<bool>? progress = null,
            CancellationToken cancel = default) where T : class, IServicePrx =>
            await proxy.IceIsAAsync(typeof(T).GetIceTypeId()!, context, progress, cancel).ConfigureAwait(false) ?
                (proxy is T t ? t : proxy.As<T>()) : null;

        /// <summary>Creates a clone of this proxy.</summary>
        /// <param name="proxy">The source proxy.</param>
        /// <returns>A clone of the source proxy.</returns>
        public static T Clone<T>(this T proxy) where T : class, IServicePrx => (proxy.Impl.Clone() as T)!;

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
        public static async ValueTask<OutgoingResponse> ForwardAsync(
            this IServicePrx proxy,
            IncomingRequest request,
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
                return new OutgoingResponse(request, response);
            }
            catch (LimitExceededException exception)
            {
                return new OutgoingResponse(request, new ServerException(exception.Message, exception));
            }
        }

        /// <summary>Returns the Connection for this proxy. If the proxy does not yet have an established connection,
        /// it first attempts to create a connection.</summary>
        /// <param name="proxy">The proxy.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The Connection for this proxy.</returns>
        public static ValueTask<Connection> GetConnectionAsync(
            this IServicePrx proxy,
            CancellationToken cancel = default) =>
            proxy.Impl.GetConnectionAsync(cancel);

        /// <summary>Retrieves the proxy factory associated with a generated service proxy using reflection.</summary>
        /// <returns>The proxy factory.</returns>
        public static IProxyFactory<T> GetFactory<T>() where T : class, IServicePrx
        {
            if (typeof(T).GetField("Factory") is FieldInfo factoryField)
            {
                return factoryField.GetValue(null) is IProxyFactory<T> factory ? factory :
                    throw new InvalidOperationException($"{typeof(T).FullName}.Factory is not a proxy factory");
            }
            else
            {
                throw new InvalidOperationException($"{typeof(T).FullName} does not have a field named Factory");
            }
        }

        /// <summary>Returns a new copy of the underlying options.</summary>
        /// <returns>An instance of the options class.</returns>
        public static ProxyOptions GetOptions(this IServicePrx proxy) => proxy.Impl.GetOptions();

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

        /// <summary>Creates a copy of this proxy with a new path and type.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="proxy">The proxy being copied.</param>
        /// <param name="path">The new path.</param>
        /// <returns>A proxy with the specified path and type.</returns>
        public static T WithPath<T>(this IServicePrx proxy, string path) where T : class, IServicePrx
        {
            if (path == proxy.Path && proxy is T t)
            {
                return t;
            }
            else if (proxy.Protocol == Protocol.Ice1)
            {
                var identity = Identity.FromPath(path);

                Endpoint? endpoint = proxy.Impl.ParsedEndpoint;
                Connection? connection = proxy.Connection;

                if (proxy.Impl.IsWellKnown)
                {
                    // Need to replace Loc endpoint since we're changing the identity.
                    endpoint = LocEndpoint.Create(identity);
                    connection = null; // clear cached connection since we're changing the endpoint
                }

                return GetFactory<T>().Create(identity,
                                              proxy.GetFacet(),
                                              proxy.Encoding,
                                              endpoint,
                                              proxy.Impl.ParsedAltEndpoints,
                                              connection,
                                              proxy.GetOptions());
            }
            else
            {
                return GetFactory<T>().Create(path,
                                              proxy.Protocol,
                                              proxy.Encoding,
                                              proxy.Impl.ParsedEndpoint,
                                              proxy.Impl.ParsedAltEndpoints,
                                              proxy.Connection,
                                              proxy.GetOptions());
            }
        }
    }
}
