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
        /// <param name="identity">The identity of the clone.</param>
        /// <param name="identityAndFacet">A URI string [category/]identity[#facet].</param>
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
            Identity? identity = null,
            string? identityAndFacet = null,
            IEnumerable<InvocationInterceptor>? invocationInterceptors = null,
            TimeSpan? invocationTimeout = null,
            object? label = null,
            IEnumerable<string>? location = null,
            ILocationService? locationService = null,
            bool? oneway = null,
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
                                                                   identity,
                                                                   identityAndFacet,
                                                                   invocationInterceptors,
                                                                   invocationTimeout,
                                                                   label,
                                                                   location,
                                                                   locationService,
                                                                   oneway,
                                                                   preferExistingConnection,
                                                                   preferNonSecure));
            return proxy is T t && t.Equals(clone) ? t : clone;
        }

        /// <summary>Creates a proxy for a service hosted by <c>server</c> with the given identity and facet. If
        /// <c>server</c> is configured with an adapter ID, the proxy is an indirect proxy with a location set to this
        /// adapter ID or the server's replica group ID (if defined). Otherwise, the proxy is a direct proxy with the
        /// server's published endpoints.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="server">The server hosting this service.</param>
        /// <param name="identity">The identity of the service.</param>
        /// <param name="facet">The facet.</param>
        /// <returns>A new service proxy.</returns>
        public static T Create<T>(this IProxyFactory<T> factory, Server server, Identity identity, string facet = "")
            where T : class, IServicePrx
        {
            string location = server.ReplicaGroupId.Length > 0 ? server.ReplicaGroupId : server.AdapterId;

            Protocol protocol =
                server.PublishedEndpoints.Count > 0 ? server.PublishedEndpoints[0].Protocol : server.Protocol;

            var options = new ServicePrxOptions()
            {
                Communicator = server.Communicator,
                Endpoints = location.Length == 0 ? server.PublishedEndpoints : ImmutableArray<Endpoint>.Empty,
                Facet = facet,
                Identity = identity,
                Location = location.Length > 0 ? ImmutableList.Create(location) : ImmutableList<string>.Empty,
                IsOneway = server.IsDatagramOnly,
                Protocol = protocol
            };

            return factory.Create(options);
        }

        public static T Create<T>(this IProxyFactory<T> factory, Server server, string identityAndFacet)
            where T : class, IServicePrx
        {
            (Identity identity, string facet) = UriParser.ParseIdentityAndFacet(identityAndFacet);
            return Create(factory, server, identity, facet);
        }

        /// <summary>Creates a proxy bound to connection, known as a fixed proxy.</summary>
        /// <paramtype name="T">The type of the new service proxy.</paramtype>
        /// <param name="factory">This proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// proxy type.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="identity">The identity of the service.</param>
        /// <param name="facet">The facet.</param>
        /// <returns>A fixed proxy.</returns>
        public static T Create<T>(
            this IProxyFactory<T> factory,
            Connection connection,
            Identity identity,
            string facet = "") where T : class, IServicePrx
        {
            var options = new ServicePrxOptions()
            {
                Communicator = connection.Communicator,
                Connection = connection,
                Identity = identity,
                Facet = facet,
                IsOneway = connection.Endpoint.IsDatagram,
                Protocol = connection.Protocol
            };

            return factory.Create(options);
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
            string facet;
            Identity identity;
            TimeSpan? invocationTimeout = null;
            object? label = null;
            IReadOnlyList<string> location;
            bool oneway = false;
            bool? preferExistingConnection = null;
            NonSecure? preferNonSecure = null;
            Protocol protocol;

            if (UriParser.IsProxyUri(proxyString))
            {
                List<string> path;
                UriParser.ProxyOptions proxyOptions;
                (endpoints, path, proxyOptions, facet) = UriParser.ParseProxy(proxyString, communicator);

                protocol = proxyOptions.Protocol ?? Protocol.Ice2;
                Debug.Assert(protocol != Protocol.Ice1); // the URI parsing rejects ice1

                encoding = proxyOptions.Encoding ?? Encoding.V20;

                switch (path.Count)
                {
                    case 0:
                        // TODO: should we add a default identity "Default" or "Root" or "Main"?
                        throw new FormatException($"missing identity in proxy `{proxyString}'");
                    case 1:
                        identity = new Identity(category: "", name: path[0]);
                        location = ImmutableArray<string>.Empty;
                        break;
                    case 2:
                        identity = new Identity(category: path[0], name: path[1]);
                        location = ImmutableArray<string>.Empty;
                        break;
                    default:
                        identity = new Identity(category: path[^2], name: path[^1]);
                        path.RemoveRange(path.Count - 2, 2);
                        location = path;
                        break;
                }

                if (identity.Name.Length == 0)
                {
                    throw new FormatException($"invalid identity with empty name in proxy `{proxyString}'");
                }
                if (location.Any(segment => segment.Length == 0))
                {
                    throw new FormatException($"invalid location with empty segment in proxy `{proxyString}'");
                }

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
                string location0;

                (identity, facet, encoding, location0, endpoints, oneway) =
                    Ice1Parser.ParseProxy(proxyString, communicator);

                // 0 or 1 segment
                location = location0.Length > 0 ? ImmutableArray.Create(location0) : ImmutableArray<string>.Empty;

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
                LocationService = endpoints.Count > 0 ? null : communicator.DefaultLocationService,
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
                throw new InvalidOperationException(
                    "cannot read a proxy from an InputStream with a null communicator");
            }

            if (istr.Encoding == Encoding.V11)
            {
                var identity = new Identity(istr);
                if (identity.Name.Length == 0)
                {
                    return null;
                }

                var proxyData = new ProxyData11(istr);

                if (proxyData.FacetPath.Length > 1)
                {
                    throw new InvalidDataException(
                        $"received proxy with {proxyData.FacetPath.Length} elements in its facet path");
                }

                if ((byte)proxyData.Protocol == 0)
                {
                    throw new InvalidDataException("received proxy with protocol set to 0");
                }

                if (proxyData.Protocol != Protocol.Ice1 && proxyData.InvocationMode != InvocationMode.Twoway)
                {
                    throw new InvalidDataException(
                        $"received proxy for protocol {proxyData.Protocol.GetName()} with invocation mode set");
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

                string location0 = endpoints.Length == 0 ? istr.ReadString() : "";

                Communicator communicator = istr.Communicator!;

                // TODO: correct unmarshaling of ice2 relative proxies (see below)

                var options = new ServicePrxOptions()
                {
                    Communicator = communicator,
                    Encoding = proxyData.Encoding,
                    Endpoints = endpoints,
                    Facet = proxyData.FacetPath.Length == 1 ? proxyData.FacetPath[0] : "",
                    Identity = identity,
                    IsOneway = proxyData.InvocationMode != InvocationMode.Twoway,
                    Location = location0.Length > 0 ? ImmutableList.Create(location0) : ImmutableList<string>.Empty,
                    LocationService = proxyData.Protocol == Protocol.Ice1 ? communicator.DefaultLocationService : null,
                    Protocol = proxyData.Protocol,
                };

                return factory.Create(options);
            }
            else
            {
                Debug.Assert(istr.Encoding == Encoding.V20);

                ProxyKind20 proxyKind = istr.ReadProxyKind20();
                if (proxyKind == ProxyKind20.Null)
                {
                    return null;
                }

                var proxyData = new ProxyData20(istr);

                if (proxyData.Identity.Name.Length == 0)
                {
                    throw new InvalidDataException("received non-null proxy with empty identity name");
                }

                Protocol protocol = proxyData.Protocol ?? Protocol.Ice2;

                if (proxyData.InvocationMode != null && protocol != Protocol.Ice1)
                {
                    throw new InvalidDataException(
                        $"received proxy for protocol {protocol.GetName()} with invocation mode set");
                }

                // The min size for an Endpoint with the 2.0 encoding is: transport (short = 2 bytes) + host name
                // (min 2 bytes as it cannot be empty) + port number (ushort, 2 bytes) + options (1 byte for empty
                // sequence), for a total of 7 bytes.
                IReadOnlyList<Endpoint> endpoints = proxyKind == ProxyKind20.Direct ?
                    istr.ReadArray(minElementSize: 7, istr => istr.ReadEndpoint(protocol)) :
                    ImmutableList<Endpoint>.Empty;

                if (proxyKind == ProxyKind20.Direct || protocol == Protocol.Ice1)
                {
                    Communicator communicator = istr.Communicator!;
                    var options = new ServicePrxOptions()
                    {
                        Communicator = communicator,
                        Encoding = proxyData.Encoding ?? Encoding.V20,
                        Endpoints = endpoints,
                        Facet = proxyData.Facet ?? "",
                        Identity = proxyData.Identity,
                        IsOneway = (proxyData.InvocationMode ?? InvocationMode.Twoway) != InvocationMode.Twoway,
                        Location = (IReadOnlyList<string>?)proxyData.Location ?? ImmutableList<string>.Empty,
                        LocationService = communicator.DefaultLocationService,
                        Protocol = protocol
                    };

                    return factory.Create(options);
                }
                else // relative proxy with protocol > ice1
                {
                    // For now, we don't support relative proxies with a location.
                    if (proxyData.Location?.Length > 0)
                    {
                        throw new InvalidDataException($"received a relative proxy with an invalid location");
                    }

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
                            Encoding = proxyData.Encoding ?? Encoding.V20,
                            Facet = proxyData.Facet ?? "",
                            Identity = proxyData.Identity,
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
                                "cannot read a relative proxy from InputStream created without a connection or proxy");
                        }

                        if (source.Protocol != protocol)
                        {
                            throw new InvalidDataException(
                                $"received a relative proxy with invalid protocol {protocol.GetName()}");
                        }

                        return factory.Clone(source,
                                             encoding: proxyData.Encoding ?? Encoding.V20,
                                             facet: proxyData.Facet ?? "",
                                             identity: proxyData.Identity);
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
