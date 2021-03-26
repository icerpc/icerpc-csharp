// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    public static class ProxyFactory
    {
        /// <summary>Tests whether a proxy points to a remote service that implements T. If so, returns a proxy of
        /// type T otherwise returns null. This is a convenience wrapper for <see cref="IServicePrx.IceIsAAsync"/>.
        /// </summary>
        /// <paramtype name="T">The type of the desired service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="proxy">The source proxy being tested.</param>
        /// <param name="context">The context dictionary for the invocation.</param>
        /// <param name="progress">Sent progress provider.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A new proxy manufactured by this proxy factory, or null.</returns>
        public static async Task<T?> CheckedCastAsync<T>(
            this IProxyFactory<T> factory,
            IServicePrx proxy,
            IReadOnlyDictionary<string, string>? context = null,
            IProgress<bool>? progress = null,
            CancellationToken cancel = default) where T : class, IServicePrx =>
            await proxy.IceIsAAsync(typeof(T).GetIceTypeId()!, context, progress, cancel).ConfigureAwait(false) ?
                (proxy is T t ? t : factory.Copy(proxy)) : null;

        /// <summary>Creates a copy of a proxy with a new proxy type specified using this proxy factory. The copy is
        /// identical to the source proxy except for the type.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="proxy">The source proxy being copied.</param>
        /// <returns>A proxy with the desired type.</returns>
        public static T Copy<T>(this IProxyFactory<T> factory, IServicePrx proxy) where T : class, IServicePrx =>
            factory.Create(proxy.Impl.GetOptions());

        /// <summary>Creates a proxy for a service hosted by <c>server</c>.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="server">The server hosting this service.</param>
        /// <param name="path">The path of the service.</param>
        /// <returns>A new service proxy.</returns>
        public static T Create<T>(this IProxyFactory<T> factory, Server server, string path)
            where T : class, IServicePrx
        {
            Protocol protocol =
                server.PublishedEndpoints.Count > 0 ? server.PublishedEndpoints[0].Protocol : server.Protocol;

            IReadOnlyList<Endpoint> endpoints = server.PublishedEndpoints;

            ProxyOptions options;

            if (protocol == Protocol.Ice1 && endpoints.Count == 0)
            {
                // Well-known proxy.
                var identity = Identity.FromPath(path);

                options = server.ProxyOptions.With(protocol.GetEncoding(),
                                                   ImmutableList.Create(LocEndpoint.Create(identity)),
                                                   facet: "",
                                                   identity,
                                                   oneway: server.IsDatagramOnly || server.ProxyOptions.IsOneway);
            }
            else
            {
                options = server.ProxyOptions.With(protocol.GetEncoding(), endpoints, path, protocol);

                if (server.IsDatagramOnly)
                {
                    options.IsOneway = true;
                }
                // else keep inherited value for IsOneway
            }
            return factory.Create(options);
        }

        /// <summary>Creates a proxy bound to connection, known as a fixed proxy.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="path">The path of the service.</param>
        /// <returns>A fixed proxy.</returns>
        public static T Create<T>(this IProxyFactory<T> factory, Connection connection, string path)
            where T : class, IServicePrx
        {
            if (connection.Server is not Server server)
            {
                throw new InvalidOperationException("cannot create a fixed proxy using a connection without a server");
            }

            ProxyOptions options = server.ProxyOptions.With(connection, path);
            if (connection.Endpoint.IsDatagram)
            {
                options.IsOneway = true;
            }

            return factory.Create(options);
        }

        // Temporary adapter
        public static T Parse<T>(this IProxyFactory<T> factory, string s, Communicator communicator)
            where T : class, IServicePrx =>
            Parse(factory, s, new ProxyOptions() { Communicator = communicator });

        /// <summary>Creates a proxy from a string and proxy options.</summary>
        public static T Parse<T>(this IProxyFactory<T> factory, string s, ProxyOptions proxyOptions)
            where T : class, IServicePrx
        {
            string proxyString = s.Trim();
            if (proxyString.Length == 0)
            {
                throw new FormatException("empty string is invalid");
            }

            return factory.Create(UriParser.IsProxyUri(proxyString) ?
                UriParser.ParseProxy(proxyString, proxyOptions) : Ice1Parser.ParseProxy(proxyString, proxyOptions));
        }

        /// <summary>Reads a proxy from the input stream.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="istr">The input stream to read from.</param>
        /// <returns>The non-null proxy read from the stream.</returns>
        public static T Read<T>(this IProxyFactory<T> factory, InputStream istr)
            where T : class, IServicePrx =>
            ReadNullable(factory, istr) ?? throw new InvalidDataException("read null for a non-nullable proxy");

        /// <summary>Reads a nullable proxy from the input stream.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="istr">The input stream to read from.</param>
        /// <returns>The proxy read from the stream, or null.</returns>
        public static T? ReadNullable<T>(this IProxyFactory<T> factory, InputStream istr)
            where T : class, IServicePrx
        {
            if (istr.Communicator == null)
            {
                throw new InvalidOperationException("cannot read a proxy from an InputStream with a null communicator");
            }

            if (istr.ProxyOptions is not ProxyOptions proxyOptions)
            {
                throw new InvalidOperationException("cannot read a proxy from an InputStream with no proxy options");
            }

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
                Endpoint[] endpoints =
                    istr.ReadArray(minElementSize: 8, istr => istr.ReadEndpoint11(proxyData.Protocol));

                if (endpoints.Length == 0)
                {
                    string adapterId = istr.ReadString();

                    // Well-known proxies are ice1-only.
                    if (proxyData.Protocol == Protocol.Ice1 || adapterId.Length > 0)
                    {
                        var locEndpoint = adapterId.Length > 0 ? LocEndpoint.Create(adapterId, proxyData.Protocol) :
                            LocEndpoint.Create(identity);

                        endpoints = new Endpoint[] { locEndpoint };
                    }
                }
                else if (endpoints.Length > 1 && endpoints.Any(endpoint => endpoint.Transport == Transport.Loc))
                {
                    throw new InvalidDataException("received multi-endpoint proxy with a loc endpoint");
                }

                if (proxyData.Protocol == Protocol.Ice1)
                {
                    Debug.Assert(endpoints.Length > 0);

                    if (proxyData.FacetPath.Length > 1)
                    {
                        throw new InvalidDataException(
                            $"received proxy with {proxyData.FacetPath.Length} elements in its facet path");
                    }

                    return CreateIce1Proxy(proxyData.Encoding,
                                           endpoints,
                                           proxyData.FacetPath.Length == 1 ? proxyData.FacetPath[0] : "",
                                           identity,
                                           proxyData.InvocationMode);
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
                    return CreateIce2Proxy(proxyData.Encoding, endpoints, identity.ToPath(), proxyData.Protocol);
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
                IReadOnlyList<Endpoint> endpoints =
                    proxyData.Endpoints?.Select(
                        data => data.ToEndpoint(istr.Communicator!, protocol))?.ToImmutableList() ??
                    ImmutableList<Endpoint>.Empty;

                if (endpoints.Count > 1 && endpoints.Any(endpoint => endpoint.Transport == Transport.Loc))
                {
                    throw new InvalidDataException("received multi-endpoint proxy with a loc endpoint");
                }

                if (protocol == Protocol.Ice1)
                {
                    InvocationMode invocationMode = endpoints.Count > 0 && endpoints.All(e => e.IsDatagram) ?
                        InvocationMode.Oneway : InvocationMode.Twoway;

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
                        throw new InvalidDataException($"received invalid ice1 identity `{proxyData.Path}'");
                    }

                    return CreateIce1Proxy(proxyData.Encoding ?? Encoding.V20,
                                           endpoints,
                                           facet,
                                           identity,
                                           invocationMode);
                }
                else
                {
                    return CreateIce2Proxy(proxyData.Encoding ?? Encoding.V20, endpoints, proxyData.Path, protocol);
                }
            }

            // Creates an ice1 proxy
            T CreateIce1Proxy(
                Encoding encoding,
                IEnumerable<Endpoint> endpoints,
                string facet,
                Identity identity,
                InvocationMode invocationMode) =>
                factory.Create(proxyOptions.With(encoding,
                                                 endpoints,
                                                 facet,
                                                 identity,
                                                 invocationMode != InvocationMode.Twoway));

            // Creates an ice2+ proxy
            T CreateIce2Proxy(Encoding encoding, IReadOnlyList<Endpoint> endpoints, string path, Protocol protocol)
            {
                ProxyOptions options = proxyOptions.With(encoding, endpoints, path, protocol);

                if (endpoints.Count == 0) // relative proxy
                {
                    if (protocol != proxyOptions.Protocol)
                    {
                        throw new InvalidDataException(
                            $"received a relative proxy with invalid protocol {protocol.GetName()}");
                    }

                    options.Connection = proxyOptions.Connection;
                    options.Endpoints = proxyOptions.Endpoints;
                    options.IsFixed = proxyOptions.IsFixed;
                }
                return factory.Create(options);
            }
        }

        /// <summary>Reads a tagged proxy from the input stream.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="istr">The input stream to read from.</param>
        /// <param name="tag">The tag.</param>
        /// <returns>The proxy read from the stream, or null.</returns>
        public static T? ReadTagged<T>(this IProxyFactory<T> factory, InputStream istr, int tag)
            where T : class, IServicePrx =>
            istr.ReadTaggedProxyHeader(tag) ? Read(factory, istr) : null;
    }
}
