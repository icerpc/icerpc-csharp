// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
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
                                              proxy.Impl.Endpoint,
                                              proxy.Impl.AltEndpoints,
                                              proxy.Connection,
                                              proxy.GetOptions());
            }
            else
            {
                return GetFactory<T>().Create(proxy.Path,
                                              proxy.Protocol,
                                              proxy.Encoding,
                                              proxy.Impl.Endpoint,
                                              proxy.Impl.AltEndpoints,
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
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A new proxy with the desired type, or null.</returns>
        public static async Task<T?> CheckedCastAsync<T>(
            this IServicePrx proxy,
            Invocation? invocation = null,
            CancellationToken cancel = default) where T : class, IServicePrx =>
            await proxy.IceIsAAsync(typeof(T).GetIceTypeId()!, invocation, cancel).ConfigureAwait(false) ?
                (proxy is T t ? t : proxy.As<T>()) : null;

        /// <summary>Creates a clone of this proxy.</summary>
        /// <param name="proxy">The source proxy.</param>
        /// <returns>A clone of the source proxy.</returns>
        public static T Clone<T>(this T proxy) where T : class, IServicePrx => (proxy.Impl.Clone() as T)!;

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

        /// <summary>Sends a request to a service and returns the response.</summary>
        /// <param name="proxy">A proxy to the target service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="requestPayload">The payload of the request.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="compress">When true, the request payload should be compressed.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="oneway">When true, the request is sent oneway and an empty response is returned immediately
        /// after sending the request.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response payload and the connection that received the response.</returns>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        public static Task<(ReadOnlyMemory<byte>, Connection)> InvokeAsync(
            this IServicePrx proxy,
            string operation,
            IList<ArraySegment<byte>> requestPayload,
            Invocation? invocation = null,
            bool compress = false,
            bool idempotent = false,
            bool oneway = false,
            CancellationToken cancel = default)
        {
            CancellationTokenSource? timeoutSource = null;
            CancellationTokenSource? combinedSource = null;

            try
            {
                DateTime deadline = invocation?.Deadline ?? DateTime.MaxValue;
                if (deadline == DateTime.MaxValue)
                {
                    TimeSpan timeout = invocation?.Timeout ?? proxy.InvocationTimeout;
                    if (timeout != Timeout.InfiniteTimeSpan)
                    {
                        deadline = DateTime.UtcNow + timeout;

                        timeoutSource = new CancellationTokenSource(timeout);
                        combinedSource = cancel.CanBeCanceled ?
                            CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutSource.Token) :
                                timeoutSource;

                        cancel = combinedSource.Token;
                    }
                    // else deadline remains MaxValue
                }
                else if (!cancel.CanBeCanceled)
                {
                    throw new ArgumentException(@$"{nameof(cancel)
                        } must be cancelable when the invocation deadline is set", nameof(cancel));
                }

                var request = new OutgoingRequest(proxy,
                                                  operation,
                                                  requestPayload,
                                                  deadline,
                                                  invocation,
                                                  compress,
                                                  idempotent,
                                                  oneway);

                // We perform as much work as possible in a non async method to throw exceptions synchronously.

                // TODO: should be simply
                // Task<IncomingResponse> responseTask = proxy.Invoker.InvokeAsync(request, cancel);
                Task<IncomingResponse> responseTask = ServicePrx.InvokeAsync(request, cancel);

                return ConvertResponseAsync(responseTask, timeoutSource, combinedSource);
            }
            catch
            {
                combinedSource?.Dispose();
                if (timeoutSource != combinedSource)
                {
                    timeoutSource?.Dispose();
                }
                throw;

                // If there is no synchronous exception, ConvertResponseAsync disposes these cancellation sources.
            }

            async Task<(ReadOnlyMemory<byte>, Connection)> ConvertResponseAsync(
                Task<IncomingResponse> responseTask,
                CancellationTokenSource? timeoutSource,
                CancellationTokenSource? combinedSource)
            {
                try
                {
                    using IncomingResponse response = await responseTask.ConfigureAwait(false);

                    if (invocation != null)
                    {
                        invocation.ResponseFeatures = response.Features;
                    }

                    // Temporary
                    if (response.PayloadCompressionFormat != CompressionFormat.Decompressed)
                    {
                        response.DecompressPayload();
                    }

                    return (response.Payload, response.Connection);
                }
                finally
                {
                    combinedSource?.Dispose();
                    if (timeoutSource != combinedSource)
                    {
                        timeoutSource?.Dispose();
                    }
                }
            }
        }

        /// <summary>Converts a proxy to a set of proxy properties.</summary>
        /// <param name="proxy">The proxy for the target service.</param>
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

                Endpoint? endpoint = proxy.Impl.Endpoint;
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
                                              proxy.Impl.AltEndpoints,
                                              connection,
                                              proxy.GetOptions());
            }
            else
            {
                return GetFactory<T>().Create(path,
                                              proxy.Protocol,
                                              proxy.Encoding,
                                              proxy.Impl.Endpoint,
                                              proxy.Impl.AltEndpoints,
                                              proxy.Connection,
                                              proxy.GetOptions());
            }
        }
    }
}
