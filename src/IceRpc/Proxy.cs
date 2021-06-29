// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Interop;
using IceRpc.Transports;
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
    /// <summary>Proxy provides extension methods for IServicePrx and ProxyFactory.</summary>
    public static class Proxy
    {
        /// <summary>The invoker that a proxy calls when its invoker is null.</summary>
        internal static IInvoker NullInvoker { get; } =
            new InlineInvoker((request, cancel) =>
                request.Connection?.InvokeAsync(request, cancel) ??
                    throw new ArgumentNullException($"{nameof(request.Connection)} is null", nameof(request)));

        /// <summary>Creates a copy of this proxy with a new proxy type.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="proxy">The proxy being copied.</param>
        /// <returns>A proxy with the desired type.</returns>
        public static T As<T>(this IServicePrx proxy) where T : class, IServicePrx
        {
            T newProxy = GetFactory<T>().Create(proxy.Path, proxy.Protocol);
            if (proxy.Protocol == Protocol.Ice1)
            {
                newProxy.Impl.Identity = proxy.Impl.Identity;
                newProxy.Impl.Facet = proxy.Impl.Facet;
            }
            newProxy.Encoding = proxy.Encoding;
            newProxy.Endpoint = proxy.Endpoint;
            newProxy.AltEndpoints = proxy.AltEndpoints;
            newProxy.Connection = proxy.Connection;
            newProxy.Invoker = proxy.Invoker;
            return newProxy;
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

        /// <summary>Creates a proxy from a connection and a path, like the generated <c>FromConnection</c> static
        /// methods.</summary>
        /// <param name="factory">The proxy factory.</param>
        /// <param name="connection">The connection of the new proxy. If it's a client connection, the endpoint of the
        /// new proxy is <see cref="Connection.RemoteEndpoint"/>; otherwise, the new proxy has no endpoint.</param>
        /// <param name="path">The path of the proxy. If null, the path is set to
        /// <see cref="ProxyFactory{T}.DefaultPath"/>.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <returns>The new proxy.</returns>
        public static T Create<T>(
            this ProxyFactory<T> factory,
            Connection connection,
            string? path = null,
            IInvoker? invoker = null) where T : class, IServicePrx
        {
            path ??= factory.DefaultPath;

            T proxy = factory.Create(path, connection.Protocol);

            ServicePrx impl = proxy.Impl;
            if (connection.Protocol == Protocol.Ice1)
            {
                impl.Identity = Identity.FromPath(path);
            }
            impl.Endpoint = connection.IsServer ? null : connection.RemoteEndpoint;
            impl.Connection = connection;
            impl.Invoker = invoker;
            return proxy;
        }

        /// <summary>Creates a proxy from a path and protocol, like the generated <c>FromPath</c> static methods.
        /// </summary>
        /// <param name="factory">The proxy factory.</param>
        /// <param name="path">The path.</param>
        /// <param name="protocol">The protocol.</param>
        /// <param name="setIdentity">When true, sets the identity of a new ice1 proxy.</param>
        /// <returns>The new proxy.</returns>
        public static T Create<T>(this ProxyFactory<T> factory, string path, Protocol protocol, bool setIdentity)
            where T : class, IServicePrx
        {
            T proxy = factory.Create(path, protocol);
            if (setIdentity && protocol == Protocol.Ice1)
            {
                proxy.Impl.Identity = Identity.FromPath(path);
            }
            return proxy;
        }

        /// <summary>Creates a proxy from a server and a path, like the generated <c>FromServer</c> static
        /// methods.</summary>
        /// <param name="factory">The proxy factory.</param>
        /// <param name="server">The server.</param>
        /// <param name="path">The path. Null uses the default path.</param>
        /// <returns>The new proxy.</returns>
        public static T Create<T>(this ProxyFactory<T> factory, Server server, string? path = null)
            where T : class, IServicePrx
        {
            path ??= factory.DefaultPath;

            if (server.ProxyEndpoint == null)
            {
                throw new InvalidOperationException("cannot create a proxy using a server with no endpoint");
            }

            T proxy = factory.Create(path, server.Protocol);

            ServicePrx impl = proxy.Impl;
            if (server.Protocol == Protocol.Ice1)
            {
                impl.Identity = Identity.FromPath(path);
            }
            impl.Endpoint = server.ProxyEndpoint;
            return proxy;
        }

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
        /// <param name="streamDataWriter">The writer to encode the stream parameter.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response payload, its encoding and the connection that received the response.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        public static Task<(ReadOnlyMemory<byte>, Encoding, Connection, RpcStream)> InvokeAsync(
            this IServicePrx proxy,
            string operation,
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload,
            Invocation? invocation = null,
            bool compress = false,
            bool idempotent = false,
            bool oneway = false,
            // TODO: the stream data writer shouldn't depend on the Stream transport API.
            Action<RpcStream>? streamDataWriter = null,
            CancellationToken cancel = default)
        {
            CancellationTokenSource? timeoutSource = null;
            CancellationTokenSource? combinedSource = null;

            if (compress)
            {
                invocation ??= new Invocation();
                invocation.RequestFeatures = invocation.RequestFeatures.CompressPayload();
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
                                                  oneway,
                                                  streamDataWriter);

                // We perform as much work as possible in a non async method to throw exceptions synchronously.
                Task<IncomingResponse> responseTask = (proxy.Invoker ?? NullInvoker).InvokeAsync(request, cancel);
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

            async Task<(ReadOnlyMemory<byte> Payload, Encoding PayloadEncoding, Connection Connection, RpcStream)> ConvertResponseAsync(
                Task<IncomingResponse> responseTask,
                CancellationTokenSource? timeoutSource,
                CancellationTokenSource? combinedSource)
            {
                try
                {
                    IncomingResponse response = await responseTask.ConfigureAwait(false);
                    ReadOnlyMemory<byte> responsePayload = await response.GetPayloadAsync(cancel).ConfigureAwait(false);

                    if (invocation != null)
                    {
                        invocation.ResponseFeatures = response.Features;
                    }

                    if (response.ResultType == ResultType.Failure)
                    {
                        throw Payload.ToRemoteException(responsePayload,
                                                        response.PayloadEncoding,
                                                        response.ReplyStatus,
                                                        response.Connection,
                                                        proxy.Invoker);
                    }

                    return (responsePayload, response.PayloadEncoding, response.Connection, response.Stream);
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

        /// <summary>Creates a proxy from a string and an invoker.</summary>
        /// <param name="proxyFactory">The proxy factory.</param>
        /// <param name="s">The string to parse.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <returns>The parsed proxy.</returns>
        public static T Parse<T>(this ProxyFactory<T> proxyFactory, string s, IInvoker? invoker = null)
            where T : class, IServicePrx
        {
            string proxyString = s.Trim();
            if (proxyString.Length == 0)
            {
                throw new FormatException("an empty string does not represent a proxy");
            }

            T proxy;
            Encoding encoding;
            Endpoint? endpoint;
            ImmutableList<Endpoint> altEndpoints;
            if (Internal.UriParser.IsProxyUri(proxyString))
            {
                string path;
                (path, encoding, endpoint, altEndpoints) = Internal.UriParser.ParseProxy(proxyString);
                proxy = proxyFactory.Create(path, endpoint?.Protocol ?? Protocol.Ice2);
            }
            else
            {
                Identity identity;
                string facet;
                (identity, facet, encoding, endpoint, altEndpoints) = Ice1Parser.ParseProxy(proxyString);
                proxy = proxyFactory.Create(identity, facet);
            }
            proxy.Encoding = encoding;
            proxy.Endpoint = endpoint;
            proxy.AltEndpoints = altEndpoints;
            proxy.Invoker = invoker;
            return proxy;
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
        /// <param name="proxyFactory">The factory used to create the proxy.</param>
        /// <param name="istr">The input stream to read from.</param>
        /// <returns>The proxy read from the stream, or null.</returns>
        public static T? ReadNullable<T>(
            this ProxyFactory<T> proxyFactory,
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
                    if (proxyData.FacetPath.Count > 1)
                    {
                        throw new InvalidDataException(
                            $"received proxy with {proxyData.FacetPath.Count} elements in its facet path");
                    }

                    try
                    {
                        T proxy = proxyFactory.Create(identity.ToPath(), Protocol.Ice1);
                        proxy.Impl.Identity = identity;
                        proxy.Impl.FacetPath = proxyData.FacetPath;
                        proxy.Encoding = proxyData.Encoding;
                        proxy.Endpoint = endpoint;
                        proxy.AltEndpoints = altEndpoints.ToImmutableList();
                        proxy.Invoker = istr.Invoker;
                        return proxy;
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
                else
                {
                    if (proxyData.FacetPath.Count > 0)
                    {
                        throw new InvalidDataException(
                            $"received proxy for protocol {proxyData.Protocol.GetName()} with facet");
                    }
                    if (proxyData.InvocationMode != InvocationMode.Twoway)
                    {
                        throw new InvalidDataException(
                            $"received proxy for protocol {proxyData.Protocol.GetName()} with invocation mode set");
                    }

                    try
                    {
                        T proxy;

                        if (endpoint == null && istr.Connection is Connection connection)
                        {
                            proxy = proxyFactory.Create(connection, identity.ToPath(), istr.Invoker);
                        }
                        else
                        {
                            proxy = proxyFactory.Create(identity.ToPath(), proxyData.Protocol);
                            proxy.Endpoint = endpoint;
                            proxy.AltEndpoints = altEndpoints.ToImmutableList();
                            proxy.Invoker = istr.Invoker;
                        }

                        proxy.Encoding = proxyData.Encoding;
                        return proxy;
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
                    ImmutableList<string> facetPath;
                    string path;

                    int hashIndex = proxyData.Path.IndexOf('#');
                    if (hashIndex == -1)
                    {
                        path = proxyData.Path;
                        facetPath = ImmutableList<string>.Empty;
                    }
                    else
                    {
                        path = proxyData.Path[0..hashIndex];
                        facetPath = ImmutableList.Create(proxyData.Path[(hashIndex + 1)..]);
                    }

                    try
                    {
                        T proxy = proxyFactory.Create(path, Protocol.Ice1, setIdentity: true);
                        proxy.Impl.FacetPath = facetPath;
                        proxy.Encoding = proxyData.Encoding ?? Encoding.V20;
                        proxy.Endpoint = endpoint;
                        proxy.AltEndpoints = altEndpoints;
                        proxy.Invoker = istr.Invoker;
                        return proxy;
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
                else
                {
                    try
                    {
                        T proxy;

                        if (endpoint == null && istr.Connection is Connection connection)
                        {
                            proxy = proxyFactory.Create(connection, proxyData.Path, istr.Invoker);
                        }
                        else
                        {
                            proxy = proxyFactory.Create(proxyData.Path, protocol);
                            proxy.Endpoint = endpoint;
                            proxy.AltEndpoints = altEndpoints;
                            proxy.Invoker = istr.Invoker;
                        }

                        proxy.Encoding = proxyData.Encoding ?? Encoding.V20;

                        return proxy;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("received invalid proxy", ex);
                    }
                }
            }
        }

        /// <summary>Reads a tagged proxy from the input stream.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="proxyFactory">The factory used to create the proxy.</param>
        /// <param name="istr">The input stream to read from.</param>
        /// <param name="tag">The tag.</param>
        /// <returns>The proxy read from the stream, or null.</returns>
        public static T? ReadTagged<T>(
            this ProxyFactory<T> proxyFactory,
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
            if (path == proxy.Path && proxy is T newProxy)
            {
                return newProxy;
            }

            newProxy = GetFactory<T>().Create(path, proxy.Protocol, setIdentity: true);
            if (proxy.Protocol == Protocol.Ice1)
            {
                newProxy.Impl.Facet = proxy.GetFacet();
                // clear cached connection of well-known proxy
                newProxy.Connection = proxy.Endpoint == null ? null : proxy.Connection;
            }
            else
            {
                newProxy.Connection = proxy.Connection;
            }

            newProxy.AltEndpoints = proxy.AltEndpoints;
            newProxy.Encoding = proxy.Encoding;
            newProxy.Endpoint = proxy.Endpoint;
            newProxy.Invoker = proxy.Invoker;
            return newProxy;
        }
    }
}
