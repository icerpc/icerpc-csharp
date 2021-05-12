// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
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
            T prx = GetFactory<T>()(proxy.Path, proxy.Protocol);
            if (proxy.Protocol == Protocol.Ice1)
            {
                prx.Impl.Identity = proxy.GetIdentity();
                prx.Impl.Facet = proxy.GetFacet();
            }
            prx.Encoding = proxy.Encoding;
            prx.Endpoint = proxy.Endpoint;
            prx.AltEndpoints = proxy.AltEndpoints;
            prx.Connection = proxy.Connection;
            prx.Invoker = proxy.Invoker;
            return prx;
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

        /// <summary>Retrieves the proxy factory associated with a generated service proxy using reflection.</summary>
        /// <returns>The proxy factory.</returns>
        public static ProxyFactory<T> GetFactory<T>() where T : class, IServicePrx
        {
            if (typeof(T).GetField("Factory") is FieldInfo factoryField)
            {
                return factoryField.GetValue(null) is ProxyFactory<T> factory ? factory :
                    throw new InvalidOperationException($"{typeof(T).FullName}.Factory is not a proxy factory");
            }
            else
            {
                throw new InvalidOperationException($"{typeof(T).FullName} does not have a field named Factory");
            }
        }

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

            if (compress && invocation?.RequestFeatures[typeof(Features.CompressPayload)] == null)
            {
                invocation ??= new Invocation();
                invocation.RequestFeatures[typeof(Features.CompressPayload)] = Features.CompressPayload.Yes;
            }

            try
            {
                DateTime deadline = invocation?.Deadline ?? DateTime.MaxValue;
                if (deadline == DateTime.MaxValue)
                {
                    TimeSpan timeout = invocation?.Timeout ?? Runtime.DefaultInvocationTimeout;
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

        /// <summary>Creates a proxy from a string and proxy options.</summary>
        public static T Parse<T>(string s, IInvoker invoker) where T : class, IServicePrx
        {
            string proxyString = s.Trim();
            if (proxyString.Length == 0)
            {
                throw new FormatException("an empty string does not represent a proxy");
            }

            T prx;
            Encoding encoding;
            Endpoint? endpoint;
            ImmutableList<Endpoint> altEndpoints;
            if (Internal.UriParser.IsProxyUri(proxyString))
            {
                string path;
                (path, encoding, endpoint, altEndpoints) = Internal.UriParser.ParseProxy(proxyString);
                prx = GetFactory<T>()(path, endpoint?.Protocol ?? Protocol.Ice2);
            }
            else
            {
                Identity identity;
                string facet;
                (identity, facet, encoding, endpoint, altEndpoints) = Ice1Parser.ParseProxy(proxyString);
                prx = GetFactory<T>().Create(identity, facet);
            }
            prx.Encoding = encoding;
            prx.Endpoint = endpoint;
            prx.AltEndpoints = altEndpoints;
            prx.Invoker = invoker;
            return prx;
        }

        /// <summary>Reads a proxy from the input stream.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="proxyFactory">A factory used to create the proxy.</param>
        /// <param name="istr">The input stream to read from.</param>
        /// <returns>The non-null proxy read from the stream.</returns>
        public static T Read<T>(
            ProxyFactory<T> proxyFactory,
            InputStream istr) where T : class, IServicePrx =>
            ReadNullable(proxyFactory, istr) ??
            throw new InvalidDataException("read null for a non-nullable proxy");

        /// <summary>Reads a nullable proxy from the input stream.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="proxyFactory">A factory used to create the proxy.</param>
        /// <param name="istr">The input stream to read from.</param>
        /// <returns>The proxy read from the stream, or null.</returns>
        public static T? ReadNullable<T>(
            ProxyFactory<T> proxyFactory,
            InputStream istr) where T : class, IServicePrx
        {
            if (istr.Encoding == Encoding.V11)
            {
                var identity = new Identity(istr);
                if (identity.Name.Length == 0) // such identity means received a null proxy with the 1.1 encoding
                {
                    return null;
                }

                var proxyData = new ProxyData11(istr);

                if ((byte)proxyData.Protocol == 0)
                {
                    throw new InvalidDataException("received proxy with protocol set to 0");
                }
                if (proxyData.ProtocolMinor != 0)
                {
                    throw new InvalidDataException(
                        $"received proxy with invalid protocolMinor value: {proxyData.ProtocolMinor}");
                }

                // The min size for an Endpoint with the 1.1 encoding is: transport (short = 2 bytes) + encapsulation
                // header (6 bytes), for a total of 8 bytes.
                Endpoint[] endpointArray =
                    istr.ReadArray(minElementSize: 8, istr => istr.ReadEndpoint11(proxyData.Protocol));

                Endpoint? endpoint = null;
                IEnumerable<Endpoint> altEndpoints;

                if (endpointArray.Length == 0)
                {
                    string adapterId = istr.ReadString();
                    if (adapterId.Length > 0)
                    {
                        endpoint = LocEndpoint.Create(adapterId, proxyData.Protocol);
                    }
                    altEndpoints = ImmutableList<Endpoint>.Empty;
                }
                else
                {
                    endpoint = endpointArray[0];
                    altEndpoints = endpointArray[1..];
                }

                if (proxyData.Protocol == Protocol.Ice1)
                {
                    if (proxyData.FacetPath.Length > 1)
                    {
                        throw new InvalidDataException(
                            $"received proxy with {proxyData.FacetPath.Length} elements in its facet path");
                    }

                    return CreateIce1Proxy(proxyData.Encoding,
                                           endpoint,
                                           altEndpoints,
                                           proxyData.FacetPath.Length == 1 ? proxyData.FacetPath[0] : "",
                                           identity);
                }
                else
                {
                    if (proxyData.FacetPath.Length > 0)
                    {
                        throw new InvalidDataException(
                            $"received proxy for protocol {proxyData.Protocol.GetName()} with facet");
                    }
                    if (proxyData.InvocationMode != InvocationMode.Twoway)
                    {
                        throw new InvalidDataException(
                            $"received proxy for protocol {proxyData.Protocol.GetName()} with invocation mode set");
                    }

                    Connection? connection = null;
                    if (endpoint == null)
                    {
                        // Use the connection endpoint if the connection is an outgoing connection
                        endpoint = istr.Connection?.IsIncoming ?? true ? null : istr.Connection.RemoteEndpoint;
                        connection = istr.Connection;
                    }

                    try
                    {
                        var prx = proxyFactory(identity.ToPath(), connection?.Protocol ?? proxyData.Protocol);
                        prx.Encoding = proxyData.Encoding;
                        prx.Endpoint = endpoint;
                        prx.AltEndpoints = altEndpoints.ToImmutableList();
                        prx.Connection = connection;
                        prx.Invoker = istr.Invoker;
                        return prx;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("received invalid proxy", ex);
                    }
                }
            }
            else
            {
                Debug.Assert(istr.Encoding == Encoding.V20);

                var proxyData = new ProxyData20(istr);

                if (proxyData.Path == null)
                {
                    return null;
                }

                Protocol protocol = proxyData.Protocol ?? Protocol.Ice2;
                var endpoint = proxyData.Endpoint?.ToEndpoint(protocol);
                ImmutableList<Endpoint> altEndpoints =
                    proxyData.AltEndpoints?.Select(
                        data => data.ToEndpoint(protocol))?.ToImmutableList() ?? ImmutableList<Endpoint>.Empty;

                if (endpoint == null && altEndpoints.Count > 0)
                {
                    throw new InvalidDataException("received proxy with only alt endpoints");
                }

                if (protocol == Protocol.Ice1)
                {
                    string facet;
                    Identity identity;

                    int hashIndex = proxyData.Path.IndexOf('#');
                    if (hashIndex == -1)
                    {
                        identity = Identity.FromPath(proxyData.Path);
                        facet = "";
                    }
                    else
                    {
                        identity = Identity.FromPath(proxyData.Path[0..hashIndex]);
                        facet = proxyData.Path[(hashIndex + 1)..];
                    }

                    if (identity.Name.Length == 0)
                    {
                        throw new InvalidDataException($"received invalid ice1 identity '{proxyData.Path}'");
                    }

                    return CreateIce1Proxy(proxyData.Encoding ?? Encoding.V20,
                                           endpoint,
                                           altEndpoints,
                                           facet,
                                           identity);
                }
                else
                {
                    Connection? connection = null;
                    if (endpoint == null)
                    {
                        // Use the connection endpoint if the connection is an outgoing connection
                        endpoint = istr.Connection?.IsIncoming ?? true ? null : istr.Connection.RemoteEndpoint;
                        connection = istr.Connection;
                    }

                    try
                    {
                        var prx = proxyFactory(proxyData.Path, connection?.Protocol ?? protocol);
                        prx.Encoding = proxyData.Encoding ?? Encoding.V20;
                        prx.Endpoint = endpoint;
                        prx.AltEndpoints = altEndpoints;
                        prx.Connection = connection;
                        prx.Invoker = istr.Invoker;
                        return prx;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("received invalid proxy", ex);
                    }
                }
            }

            // Creates an ice1 proxy
            T CreateIce1Proxy(
                Encoding encoding,
                Endpoint? endpoint,
                IEnumerable<Endpoint> altEndpoints,
                string facet,
                Identity identity)
            {
                // For interop with ZeroC Ice, an ice1 endpointless proxy is unmarshaled as an endpointless and
                // connectionless proxy - a "well-known proxy".

                try
                {
                    var prx = proxyFactory.Create(identity, facet);
                    prx.Encoding = encoding;
                    prx.Endpoint = endpoint;
                    prx.AltEndpoints = altEndpoints.ToImmutableList();
                    prx.Invoker = istr.Invoker;
                    return prx;
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("received invalid proxy", ex);
                }
            }
        }

        /// <summary>Reads a tagged proxy from the input stream.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="proxyFactory">A factory used to create the proxy.</param>
        /// <param name="istr">The input stream to read from.</param>
        /// <param name="tag">The tag.</param>
        /// <returns>The proxy read from the stream, or null.</returns>
        public static T? ReadTagged<T>(
            ProxyFactory<T> proxyFactory,
            InputStream istr,
            int tag)
            where T : class, IServicePrx =>
            istr.ReadTaggedProxyHeader(tag) ? Read(proxyFactory, istr) : null;

        /// <summary>Creates a copy of this proxy with a new path and type.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="proxy">The proxy being copied.</param>
        /// <param name="path">The new path.</param>
        /// <returns>A proxy with the specified path and type.</returns>
        public static T WithPath<T>(this IServicePrx proxy, string path) where T : class, IServicePrx
        {
            if (path == proxy.Path && proxy is T prx)
            {
                return prx;
            }

            prx = GetFactory<T>()(path, proxy.Protocol);
            if (proxy.Protocol == Protocol.Ice1)
            {
                prx.Impl.Identity = Identity.FromPath(path);
                prx.Impl.Facet = proxy.GetFacet();
                prx.Endpoint = proxy.Endpoint;
                // clear cached connection of well-known proxy
                prx.Connection = proxy.Impl.IsWellKnown ? null : proxy.Connection;
            }
            else
            {
                prx = GetFactory<T>()(path, proxy.Protocol);
                prx.Endpoint = proxy.Endpoint;
                prx.Connection = proxy.Connection;
            }
            prx.Encoding = proxy.Encoding;
            prx.AltEndpoints = proxy.AltEndpoints;
            prx.Invoker = proxy.Invoker;
            return prx;
        }

        /// <summary>Creates an ice1 proxy with the given identity and facet.</summary>
        /// <typeparam name="T">The proxy type.</typeparam>
        /// <param name="proxyFactory">A factory used to create the proxy.</param>
        /// <param name="identity">The proxy identity.</param>
        /// <param name="facet">The proxy facet.</param>
        /// <returns>A proxy with the given identity and facet.</returns>
        private static T Create<T>(
            this ProxyFactory<T> proxyFactory,
            Identity identity,
            string facet) where T : class, IServicePrx
        {
            T prx = proxyFactory(identity.ToPath(), Protocol.Ice1);
            prx.Impl.Identity = identity;
            prx.Impl.Facet = facet;
            return prx;
        }
    }
}
