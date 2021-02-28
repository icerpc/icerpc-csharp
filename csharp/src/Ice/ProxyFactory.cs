// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
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
                (proxy is T t ? t : factory.Clone(proxy)) : null;

        /// <summary>Creates a clone of a proxy with a new proxy type specified using this proxy factory. The clone is
        /// identical to the source proxy except for options set through parameters. This method returns the source
        /// proxy instead of a new proxy in the event none of the options specified through the parameters change
        /// anything and the source proxy is a T.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="proxy">The source proxy.</param>
        /// <param name="cacheConnection">Determines whether or not the clone caches its connection (optional).</param>
        /// <param name="clearLabel">When set to true, the clone does not have an associated label (optional).</param>
        /// <param name="clearLocationService">When set to true, the clone does not have an associated location service
        /// (optional).</param>
        /// <param name="context">The context of the clone (optional).</param>
        /// <param name="encoding">The encoding of the clone (optional).</param>
        /// <param name="endpoints">The endpoints of the clone (optional).</param>
        /// <param name="facet">The facet of the clone (optional).</param>
        /// <param name="fixedConnection">The connection of the clone (optional). When specified, the clone is a fixed
        /// proxy. You can clone a non-fixed proxy into a fixed proxy but not vice-versa.</param>
        /// <param name="invocationInterceptors">A collection of <see cref="InvocationInterceptor"/> that will be
        /// executed with each invocation</param>
        /// <param name="invocationTimeout">The invocation timeout of the clone (optional).</param>
        /// <param name="label">The label of the clone (optional).</param>
        /// <param name="location">The location of the clone (optional).</param>
        /// <param name="locationService">The location service of the clone (optional).</param>
        /// <param name="oneway">Determines whether the clone is oneway or twoway (optional).</param>
        /// <param name="path">The path of the clone (optional).</param>
        /// <param name="preferExistingConnection">Determines whether or not the clone prefer using an existing
        /// connection.</param>
        /// <param name="preferNonSecure">Determines whether the clone prefers non-secure connections over secure
        /// connections (optional).</param>
        /// <returns>A new proxy manufactured by the proxy factory (see factory parameter).</returns>
        public static T Clone<T>(
            this IProxyFactory<T> factory,
            IServicePrx proxy,
            bool? cacheConnection = null,
            bool clearLabel = false,
            bool clearLocationService = false,
            IReadOnlyDictionary<string, string>? context = null,
            Encoding? encoding = null,
            IEnumerable<Endpoint>? endpoints = null,
            string? facet = null,
            Connection? fixedConnection = null,
            IEnumerable<InvocationInterceptor>? invocationInterceptors = null,
            TimeSpan? invocationTimeout = null,
            object? label = null,
            string? location = null,
            ILocationService? locationService = null,
            bool? oneway = null,
            string? path = null,
            bool? preferExistingConnection = null,
            NonSecure? preferNonSecure = null) where T : class, IServicePrx
        {
            T clone = factory.Create(proxy.Impl.CreateCloneOptions(cacheConnection,
                                                                   clearLabel,
                                                                   clearLocationService,
                                                                   context,
                                                                   encoding,
                                                                   endpoints,
                                                                   facet,
                                                                   fixedConnection,
                                                                   invocationInterceptors,
                                                                   invocationTimeout,
                                                                   label,
                                                                   location,
                                                                   locationService,
                                                                   oneway,
                                                                   path,
                                                                   preferExistingConnection,
                                                                   preferNonSecure));
            return proxy is T t && t.Equals(clone) ? t : clone;
        }

        /// <summary>Creates a proxy for a service hosted by <c>server</c>. If <c>server</c> is configured with an
        /// adapter ID, the proxy is an indirect proxy with a location set to this adapter ID or the server's replica
        /// group ID (if defined). Otherwise, the proxy is a direct proxy with the server's published endpoints.
        /// </summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="server">The server hosting this service.</param>
        /// <param name="path">The path of the service.</param>
        /// <param name="facet">The facet (optional, ice1 only).</param>
        /// <returns>A new service proxy.</returns>
        public static T Create<T>(this IProxyFactory<T> factory, Server server, string path, string facet = "")
            where T : class, IServicePrx
        {
            string location = server.ReplicaGroupId.Length > 0 ? server.ReplicaGroupId : server.AdapterId;

            Protocol protocol =
                server.PublishedEndpoints.Count > 0 ? server.PublishedEndpoints[0].Protocol : server.Protocol;

            if (facet.Length > 0 && protocol != Protocol.Ice1)
            {
                throw new ArgumentException("facet must be empty when the protocol is not ice1", nameof(facet));
            }

            if (location.Length > 0 && protocol != Protocol.Ice1)
            {
                throw new ArgumentException("location must be empty when the protocol is not ice1", nameof(facet));
            }

            if (protocol == Protocol.Ice1)
            {
                var options = new ServicePrxOptions()
                {
                    Communicator = server.Communicator,
                    Endpoints = location.Length == 0 ? server.PublishedEndpoints : ImmutableList<Endpoint>.Empty,
                    Facet = facet,
                    Location = location,
                    IsOneway = server.IsDatagramOnly,
                    Path = Proxy.NormalizePath(path),
                    Protocol = Protocol.Ice1
                };
                return factory.Create(options);
            }
            else
            {
                var options = new ServicePrxOptions()
                {
                    Communicator = server.Communicator,
                    Endpoints = server.PublishedEndpoints,
                    Path = Proxy.NormalizePath(path),
                    Protocol = protocol
                };
                return factory.Create(options);
            }
        }

        /// <summary>Creates a proxy bound to connection, known as a fixed proxy.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="path">The path of the service.</param>
        /// <param name="facet">The facet (optional, ice1 only).</param>
        /// <returns>A fixed proxy.</returns>
        public static T Create<T>(
            this IProxyFactory<T> factory,
            Connection connection,
            string path,
            string facet = "") where T : class, IServicePrx
        {
            Protocol protocol = connection.Protocol;

            if (protocol == Protocol.Ice1)
            {
                var options = new ServicePrxOptions()
                {
                    Communicator = connection.Communicator,
                    Connection = connection,
                    Facet = facet,
                    IsOneway = connection.Endpoint.IsDatagram,
                    Path = Proxy.NormalizePath(path),
                    Protocol = Protocol.Ice1
                };
                return factory.Create(options);
            }
            else
            {
                var options = new ServicePrxOptions()
                {
                    Communicator = connection.Communicator,
                    Connection = connection,
                    Path = Proxy.NormalizePath(path),
                    Protocol = protocol
                };
                return factory.Create(options);
            }
        }

        /// <summary>Creates a proxy from a string and a communicator.</summary>
        public static T Parse<T>(
            this IProxyFactory<T> factory,
            string s,
            Communicator communicator,
            string? propertyPrefix = null) where T : class, IServicePrx
        {
            string proxyString = s.Trim();
            if (proxyString.Length == 0)
            {
                throw new FormatException("empty string is invalid");
            }

            bool? cacheConnection = null;
            IReadOnlyDictionary<string, string>? context = null;
            Encoding encoding;
            IReadOnlyList<Endpoint> endpoints;
            string facet = "";
            Identity identity = default;
            TimeSpan? invocationTimeout = null;
            object? label = null;
            string location = "";
            bool oneway = false;
            string path = "";
            bool? preferExistingConnection = null;
            NonSecure? preferNonSecure = null;
            Protocol protocol;

            if (UriParser.IsProxyUri(proxyString))
            {
                UriParser.ProxyOptions proxyOptions;
                (endpoints, path, proxyOptions) = UriParser.ParseProxy(proxyString, communicator);

                protocol = proxyOptions.Protocol ?? Protocol.Ice2;
                Debug.Assert(protocol != Protocol.Ice1); // the URI parsing rejects ice1

                encoding = proxyOptions.Encoding ?? Encoding.V20;

                (cacheConnection,
                 context,
                 invocationTimeout,
                 label,
                 preferExistingConnection,
                 preferNonSecure) = proxyOptions;
            }
            else
            {
                protocol = Protocol.Ice1;

                (identity, facet, encoding, location, endpoints, oneway) =
                    Ice1Parser.ParseProxy(proxyString, communicator);

                // Override the defaults with the proxy properties if a property prefix is defined.
                if (propertyPrefix != null && propertyPrefix.Length > 0)
                {
                    cacheConnection = communicator.GetPropertyAsBool($"{propertyPrefix}.CacheConnection");

                    string property = $"{propertyPrefix}.Context.";
                    context = communicator.GetProperties(forPrefix: property).
                        ToImmutableDictionary(e => e.Key[property.Length..], e => e.Value);

                    property = $"{propertyPrefix}.InvocationTimeout";
                    invocationTimeout = communicator.GetPropertyAsTimeSpan(property);
                    if (invocationTimeout == TimeSpan.Zero)
                    {
                        throw new InvalidConfigurationException($"{property}: 0 is not a valid value");
                    }

                    label = communicator.GetProperty($"{propertyPrefix}.Label");

                    preferNonSecure = communicator.GetPropertyAsEnum<NonSecure>($"{propertyPrefix}.PreferNonSecure");
                }
            }

            var options = new ServicePrxOptions()
            {
                CacheConnection = cacheConnection ?? true,
                Communicator = communicator,
                Context = context,
                Encoding = encoding,
                Endpoints = endpoints,
                Facet = facet,
                Identity = identity,
                InvocationTimeoutOverride = invocationTimeout,
                IsOneway = oneway,
                Location = location,
                LocationService = protocol == Protocol.Ice1 && endpoints.Count == 0 ?
                    communicator.DefaultLocationService : null,
                Path = path,
                PreferExistingConnectionOverride = preferExistingConnection,
                PreferNonSecureOverride = preferNonSecure,
                Protocol = protocol
            };

            return factory.Create(options);
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

            if (istr.Encoding == Encoding.V11)
            {
                var identity = new Identity(istr);
                if (identity.Name.Length == 0)
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
                    istr.ReadArray(minElementSize: 8, istr => istr.ReadEndpoint(proxyData.Protocol));

                string location = endpoints.Length == 0 ? istr.ReadString() : "";

                if (proxyData.Protocol == Protocol.Ice1)
                {
                    if (proxyData.FacetPath.Length > 1)
                    {
                        throw new InvalidDataException(
                            $"received proxy with {proxyData.FacetPath.Length} elements in its facet path");
                    }

                    return CreateIce1Proxy(proxyData.Encoding,
                                           endpoints,
                                           proxyData.FacetPath.Length == 1 ? proxyData.FacetPath[0] : "",
                                           identity,
                                           proxyData.InvocationMode,
                                           location);
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

                ProxyKind20 proxyKind = istr.ReadProxyKind20();
                if (proxyKind == ProxyKind20.Null)
                {
                    return null;
                }

                if (proxyKind == ProxyKind20.Direct || proxyKind == ProxyKind20.Relative) // an ice2+ proxy
                {
                    var proxyData = new ProxyData20(istr);
                    Protocol protocol = proxyData.Protocol ?? Protocol.Ice2;
                    IReadOnlyList<Endpoint> endpoints = ImmutableList<Endpoint>.Empty;

                    if (proxyKind == ProxyKind20.Direct)
                    {
                        // The min size for an Endpoint with the 2.0 encoding is: transport (short = 2 bytes) + hostname
                        // (min 2 bytes as it cannot be empty) + port number (ushort, 2 bytes) + options (1 byte for
                        // empty sequence), for a total of 7 bytes.
                        endpoints = istr.ReadArray(minElementSize: 7, istr => istr.ReadEndpoint(protocol));

                        if (endpoints.Count == 0)
                        {
                            throw new InvalidDataException("received a direct proxy with no endpoint");
                        }
                    }
                    return CreateIce2Proxy(proxyData.Encoding ?? Encoding.V20, endpoints, proxyData.Path, protocol);
                }
                else // an ice1 proxy
                {
                    var proxyData = new Ice1ProxyData20(istr);

                    if (proxyData.Identity.Name.Length == 0)
                    {
                        throw new InvalidDataException("received non-null proxy with empty identity name");
                    }

                    IReadOnlyList<Endpoint> endpoints = ImmutableList<Endpoint>.Empty;
                    string location = "";

                    if (proxyKind == ProxyKind20.Ice1Direct)
                    {
                        endpoints = istr.ReadArray(minElementSize: 7, istr => istr.ReadEndpoint(Protocol.Ice1));

                        if (endpoints.Count == 0)
                        {
                            throw new InvalidDataException("received a direct proxy with no endpoint");
                        }
                    }
                    else // ice1 indirect proxy
                    {
                        location = istr.ReadString();
                    }

                    return CreateIce1Proxy(proxyData.Encoding ?? Encoding.V11,
                                           endpoints,
                                           proxyData.Facet ?? "",
                                           proxyData.Identity,
                                           proxyData.InvocationMode ?? InvocationMode.Twoway,
                                           location);
                }
            }

            // Creates an ice1 proxy
            T CreateIce1Proxy(
                Encoding encoding,
                IReadOnlyList<Endpoint> endpoints,
                string facet,
                Identity identity,
                InvocationMode invocationMode,
                string location)
            {
                Communicator communicator = istr.Communicator!;

                var options = new ServicePrxOptions()
                {
                    Communicator = communicator,
                    Encoding = encoding,
                    Endpoints = endpoints,
                    Facet = facet,
                    Identity = identity,
                    IsOneway = invocationMode != InvocationMode.Twoway,
                    Location = location,
                    LocationService = location.Length > 0 ? communicator.DefaultLocationService : null,
                    Protocol = Protocol.Ice1
                };
                return factory.Create(options);
            }

            // Creates an ice2+ proxy
            T CreateIce2Proxy(Encoding encoding, IReadOnlyList<Endpoint> endpoints, string path, Protocol protocol)
            {
                if (endpoints.Count > 0)
                {
                    var options = new ServicePrxOptions()
                    {
                        Communicator = istr.Communicator!,
                        Encoding = encoding,
                        Endpoints = endpoints,
                        Path = path,
                        Protocol = protocol
                    };
                    return factory.Create(options);
                }
                else // relative proxy
                {
                    if (istr.Connection is Connection connection)
                    {
                        if (connection.Protocol != protocol)
                        {
                            throw new InvalidDataException(
                                $"received a relative proxy with invalid protocol {protocol.GetName()}");
                        }

                        var options = new ServicePrxOptions()
                        {
                            Communicator = connection.Communicator,
                            Connection = connection,
                            Encoding = encoding,
                            Path = path,
                            Protocol = protocol
                        };

                        return factory.Create(options);
                    }
                    else
                    {
                        ServicePrx? source = istr.SourceProxy;

                        if (source == null)
                        {
                            throw new InvalidOperationException(
                                "cannot read a relative proxy from an InputStream created without a connection or proxy");
                        }

                        if (source.Protocol != protocol)
                        {
                            throw new InvalidDataException(
                                $"received a relative proxy with invalid protocol {protocol.GetName()}");
                        }

                        return factory.Clone(source,
                                             encoding: encoding,
                                             path: path);
                    }
                }
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
