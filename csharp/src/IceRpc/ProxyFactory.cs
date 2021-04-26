// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace IceRpc
{
    public static class ProxyFactory
    {
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
            if (connection.Dispatcher == null)
            {
                throw new InvalidOperationException(
                    "cannot create a fixed proxy using a connection without a dispatcher");
            }

            ProxyOptions options = connection.Server?.ProxyOptions ?? new ProxyOptions();
            if (connection.Endpoint.IsDatagram && !options.IsOneway)
            {
                options = options.Clone();
                options.IsOneway = true;
            }

            return factory.Create(path,
                                  connection.Protocol,
                                  connection.Protocol.GetEncoding(),
                                  null,
                                  ImmutableList<Endpoint>.Empty,
                                  connection,
                                  options);
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
                throw new FormatException("an empty string does not represent a proxy");
            }

            if (UriParser.IsProxyUri(proxyString))
            {
                var args = UriParser.ParseProxy(proxyString, proxyOptions);
                Protocol protocol = args.Endpoint?.Protocol ?? Protocol.Ice2;
                return factory.Create(args.Path,
                                      protocol,
                                      args.Encoding,
                                      args.Endpoint,
                                      args.AltEndpoints,
                                      connection: null,
                                      args.Options);
            }
            else
            {
                var args = Ice1Parser.ParseProxy(proxyString, proxyOptions);
                return factory.Create(args.Identity,
                                      args.Facet,
                                      args.Encoding,
                                      args.Endpoint,
                                      args.AltEndpoints,
                                      connection: null,
                                      args.Options);
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
            IServicePrx? source = istr.Source;
            Connection? connection = istr.Connection ?? source?.Connection;
            ProxyOptions? proxyOptions = istr.ProxyOptions ?? source?.GetOptions();

            if ((source == null && connection == null) || proxyOptions == null)
            {
                throw new InvalidOperationException(
                    "cannot read a proxy from an InputStream with no source nor connection/proxy options");
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
                Endpoint[] endpointArray =
                    istr.ReadArray(minElementSize: 8, istr => istr.ReadEndpoint11(proxyData.Protocol));

                Endpoint? endpoint = null;
                IEnumerable<Endpoint> altEndpoints = ImmutableList<Endpoint>.Empty;

                if (endpointArray.Length == 0)
                {
                    string adapterId = istr.ReadString();
                    if (adapterId.Length > 0)
                    {
                        endpoint = LocEndpoint.Create(adapterId, proxyData.Protocol);
                    }
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
                    return CreateIce2Proxy(proxyData.Encoding,
                                           endpoint,
                                           altEndpoints,
                                           identity.ToPath(),
                                           proxyData.Protocol);
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

                if (protocol == Protocol.Ice1)
                {
                    InvocationMode invocationMode = endpoint != null && endpoint.IsDatagram ?
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
                        throw new InvalidDataException($"received invalid ice1 identity '{proxyData.Path}'");
                    }

                    return CreateIce1Proxy(proxyData.Encoding ?? Encoding.V20,
                                           endpoint,
                                           altEndpoints,
                                           facet,
                                           identity,
                                           invocationMode);
                }
                else
                {
                    return CreateIce2Proxy(proxyData.Encoding ?? Encoding.V20,
                                           endpoint,
                                           altEndpoints,
                                           proxyData.Path,
                                           protocol);
                }
            }

            // Creates an ice1 proxy
            T CreateIce1Proxy(
                Encoding encoding,
                Endpoint? endpoint,
                IEnumerable<Endpoint> altEndpoints,
                string facet,
                Identity identity,
                InvocationMode invocationMode)
            {
                ProxyOptions options = proxyOptions;
                if (options.IsOneway != (invocationMode != InvocationMode.Twoway))
                {
                    options = options.Clone();
                    options.IsOneway = invocationMode != InvocationMode.Twoway;
                }

                try
                {
                    // If there is no location resolver, it's a relative proxy.
                    if (endpoint == null && options.LocationResolver == null)
                    {
                        // The protocol of the source proxy/connection prevails.
                        Protocol protocol = connection?.Protocol ?? source!.Protocol;
                        endpoint = source?.Impl.ParsedEndpoint; // overwrite endpoints
                        altEndpoints = source?.Impl.ParsedAltEndpoints ?? altEndpoints;

                        if (protocol != Protocol.Ice1)
                        {
                            if (facet.Length > 0)
                            {
                                throw new InvalidDataException(
                                    @$"received a relative proxy with a facet on an {protocol.GetName()
                                    } connection or proxy");
                            }

                            return factory.Create(identity.ToPath(),
                                                  protocol,
                                                  encoding,
                                                  endpoint,
                                                  altEndpoints,
                                                  connection,
                                                  options);
                        }
                        else
                        {
                            return
                                factory.Create(identity, facet, encoding, endpoint, altEndpoints, connection, options);
                        }
                    }
                    else
                    {
                        if (endpoint == null)
                        {
                            endpoint = LocEndpoint.Create(identity); // well-known proxy
                        }

                        return factory.Create(identity, facet, encoding, endpoint, altEndpoints, null, options);
                    }
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

            // Creates an ice2+ proxy
            T CreateIce2Proxy(
                Encoding encoding,
                Endpoint? endpoint,
                IEnumerable<Endpoint> altEndpoints,
                string path,
                Protocol protocol)
            {
                try
                {
                    if (endpoint == null) // relative proxy
                    {
                        // The protocol of the source proxy/connection prevails. It could be for example ice1.
                        protocol = connection?.Protocol ?? source!.Protocol;
                        endpoint = source?.Impl.ParsedEndpoint; // overwrite endpoints
                        altEndpoints = source?.Impl.ParsedAltEndpoints ?? altEndpoints;
                        return factory.Create(path, protocol, encoding, endpoint, altEndpoints, connection, proxyOptions);
                    }
                    else
                    {
                        return factory.Create(path,
                                              protocol,
                                              encoding,
                                              endpoint,
                                              altEndpoints,
                                              connection: null,
                                              proxyOptions);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("received invalid proxy", ex);
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
