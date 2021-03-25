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
            if (protocol == Protocol.Ice1 && endpoints.Count == 0)
            {
                // Well-known proxy.
                var identity = Identity.FromPath(path);
                var options = new InteropServicePrxOptions()
                {
                    Communicator = server.Communicator,
                    Endpoints = ImmutableList.Create(LocEndpoint.Create(identity)),
                    Identity = identity,
                    IsOneway = server.IsDatagramOnly
                };
                return factory.Create(options);
            }
            else
            {
                var options = new ServicePrxOptions()
                {
                    Communicator = server.Communicator,
                    Endpoints = endpoints,
                    IsOneway = server.IsDatagramOnly,
                    Path = path,
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
        /// <returns>A fixed proxy.</returns>
        public static T Create<T>(this IProxyFactory<T> factory, Connection connection, string path)
            where T : class, IServicePrx
        {
            Protocol protocol = connection.Protocol;

            if (protocol == Protocol.Ice1)
            {
                var options = new ServicePrxOptions()
                {
                    Communicator = connection.Communicator!,
                    Connection = connection,
                    IsFixed = true,
                    IsOneway = connection.Endpoint.IsDatagram,
                    Path = path,
                    Protocol = Protocol.Ice1
                };
                return factory.Create(options);
            }
            else
            {
                var options = new ServicePrxOptions()
                {
                    Communicator = connection.Communicator!,
                    Connection = connection,
                    IsFixed = true,
                    Path = path,
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

            if (UriParser.IsProxyUri(proxyString))
            {
                return factory.Create(UriParser.ParseProxy(proxyString, communicator));
            }
            else
            {
                InteropServicePrxOptions options = Ice1Parser.ParseProxy(proxyString, communicator);
                Debug.Assert(options.Endpoints.Count > 0);

                // Override the defaults with the proxy properties if a property prefix is defined.
                if (propertyPrefix != null && propertyPrefix.Length > 0)
                {
                    options.CacheConnection =
                        communicator.GetPropertyAsBool($"{propertyPrefix}.CacheConnection") ?? true;

                    string property = $"{propertyPrefix}.Context.";
                    options.Context = communicator.GetProperties(forPrefix: property).
                        ToImmutableDictionary(e => e.Key[property.Length..], e => e.Value);

                    options.InvocationTimeout =
                        communicator.GetPropertyAsTimeSpan($"{propertyPrefix}.InvocationTimeout") ??
                            ServicePrxOptions.DefaultInvocationTimeout;

                    options.Label = communicator.GetProperty($"{propertyPrefix}.Label");
                    options.PreferNonSecure =
                        communicator.GetPropertyAsEnum<NonSecure>($"{propertyPrefix}.PreferNonSecure") ??
                            NonSecure.Always;
                }

                return factory.Create(options);
            }
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
                IReadOnlyList<Endpoint> endpoints,
                string facet,
                Identity identity,
                InvocationMode invocationMode)
            {
                var options = new InteropServicePrxOptions()
                {
                    CacheConnection = istr.ProxyOptions?.CacheConnection ?? true,
                    Communicator = istr.Communicator!,
                    // Connection remains null
                    Context = istr.ProxyOptions?.Context ?? ImmutableDictionary<string, string>.Empty,
                    Encoding = encoding,
                    Endpoints = endpoints,
                    Facet = facet,
                    Identity = identity,
                    InvocationInterceptors =
                        istr.ProxyOptions?.InvocationInterceptors ?? ImmutableList<InvocationInterceptor>.Empty,
                    InvocationTimeout =
                        istr.ProxyOptions?.InvocationTimeout ?? ServicePrxOptions.DefaultInvocationTimeout,
                    // IsFixed remains false
                    IsOneway = invocationMode != InvocationMode.Twoway,
                    Label = istr.ProxyOptions?.Label,
                    LocationResolver = istr.ProxyOptions?.LocationResolver,
                    PreferExistingConnection = istr.ProxyOptions?.PreferExistingConnection ?? true,
                    PreferNonSecure = istr.ProxyOptions?.PreferNonSecure ?? NonSecure.Always
                };
                return factory.Create(options);
            }

            // Creates an ice2+ proxy
            T CreateIce2Proxy(Encoding encoding, IReadOnlyList<Endpoint> endpoints, string path, Protocol protocol)
            {
                var options = new ServicePrxOptions()
                {
                    CacheConnection = istr.ProxyOptions?.CacheConnection ?? true,
                    Communicator = istr.Communicator!,
                    // Connection remains null
                    Context = istr.ProxyOptions?.Context ?? ImmutableDictionary<string, string>.Empty,
                    Encoding = encoding,
                    Endpoints = endpoints,
                    InvocationInterceptors =
                        istr.ProxyOptions?.InvocationInterceptors ?? ImmutableList<InvocationInterceptor>.Empty,
                    InvocationTimeout = istr.ProxyOptions?.InvocationTimeout ??
                        ServicePrxOptions.DefaultInvocationTimeout,
                    // IsFixed remains false
                    IsOneway = istr.ProxyOptions?.IsOneway ?? false,
                    Label = istr.ProxyOptions?.Label,
                    LocationResolver = istr.ProxyOptions?.LocationResolver,
                    Path = path,
                    PreferExistingConnection = istr.ProxyOptions?.PreferExistingConnection ?? true,
                    PreferNonSecure = istr.ProxyOptions?.PreferNonSecure ?? NonSecure.Always,
                    Protocol = protocol
                };

                if (endpoints.Count == 0) // relative proxy
                {
                    ServicePrxOptions? source = istr.ProxyOptions;

                    if (source == null)
                    {
                        throw new InvalidOperationException("cannot read a relative proxy from this input stream");
                    }

                    if (source.Protocol != protocol)
                    {
                        throw new InvalidDataException(
                            $"received a relative proxy with invalid protocol {protocol.GetName()}");
                    }

                    options.Connection = source.Connection;
                    options.Endpoints = source.Endpoints;
                    options.IsFixed = source.IsFixed;

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
