// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;

namespace IceRpc
{
    /// <summary>Provides helper methods to parse proxy and endpoint strings in the URI format.</summary>
    internal static class UriParser
    {
        /// <summary>The proxy options parsed by the UriParser.</summary>
        private struct ParsedOptions
        {
            internal bool? CacheConnection;

            internal SortedDictionary<string, string>? Context;

            internal Encoding? Encoding;
            internal TimeSpan? InvocationTimeout;

            internal bool? IsOneway;
            internal object? Label;
            internal bool? PreferExistingConnection;
            internal NonSecure? PreferNonSecure;
            internal Protocol? Protocol;
        }

        // Common options for the generic URI parsers registered for the ice and ice+transport schemes.
        private const GenericUriParserOptions ParserOptions =
            GenericUriParserOptions.DontUnescapePathDotsAndSlashes |
            GenericUriParserOptions.Idn |
            GenericUriParserOptions.IriParsing |
            GenericUriParserOptions.NoFragment |
            GenericUriParserOptions.NoUserInfo;

        /// <summary>Checks if a string is an ice+transport URI, and not an endpoint string using the ice1 string
        /// format.</summary>
        /// <param name="s">The string to check.</param>
        /// <returns>True when the string is most likely an ice+transport URI; otherwise, false.</returns>
        internal static bool IsEndpointUri(string s) =>
            s.StartsWith("ice+", StringComparison.InvariantCulture) && s.Contains("://");

        /// <summary>Checks if a string is an ice or ice+transport URI, and not a proxy string using the ice1 string
        /// format.</summary>
        /// <param name="s">The string to check.</param>
        /// <returns>True when the string is most likely an ice or ice+transport URI; otherwise, false.</returns>
        internal static bool IsProxyUri(string s) =>
            s.StartsWith("ice:", StringComparison.InvariantCulture) || IsEndpointUri(s);

        /// <summary>Checks if <c>path</c> contains only unreserved characters, %, or reserved characters other than ?.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if <c>path</c> is a valid path; otherwise, false.</returns>
        internal static bool IsValidPath(string path)
        {
            const string invalidChars = "\"<>?\\^`{|}";

            foreach (char c in path)
            {
                if (c.CompareTo('\x20') <= 0 || c.CompareTo('\x7F') >= 0 || invalidChars.Contains(c))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Makes sure path is valid and adds a leading slash to path if it does not have one already.
        /// </summary>
        internal static string NormalizePath(string path)
        {
            if (!IsValidPath(path))
            {
                throw new FormatException(
                    @$"invalid path `{path
                    }'; a valid path can only contain unreserved characters, `%' and reserved characters other than `?'");
            }

            return path.Length > 0 && path[0] == '/' ? path : $"/{path}";
        }

        /// <summary>Parses an ice+transport URI string that represents one or more server endpoints.</summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <param name="communicator">The communicator.</param>
        /// <param name="serverEndpoints">When true (the default), endpointString corresponds to the Endpoints property of
        /// a server. Otherwise, false.</param>
        /// <returns>The list of endpoints.</returns>
        internal static IReadOnlyList<Endpoint> ParseEndpoints(
            string uriString,
            Communicator communicator,
            bool serverEndpoints = true) =>
            Parse(uriString, serverEndpoints, communicator).Endpoints;

        /// <summary>Parses an ice or ice+transport URI string that represents a proxy.</summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <param name="communicator">The communicator.</param>
        /// <returns>A service options instance.</returns>
        internal static ServicePrxOptions ParseProxy(string uriString, Communicator communicator)
        {
            (Uri uri, IReadOnlyList<Endpoint> endpoints, ParsedOptions parsedOptions) =
                Parse(uriString, serverEndpoints: false, communicator);

            Debug.Assert(uri.AbsolutePath.Length > 0 && uri.AbsolutePath[0] == '/' && IsValidPath(uri.AbsolutePath));

            return new()
            {
                CacheConnection = parsedOptions.CacheConnection ?? true,
                Communicator = communicator,
                Context = parsedOptions.Context?.ToImmutableSortedDictionary(),
                Encoding = parsedOptions.Encoding ?? Encoding.V20,
                Endpoints = endpoints,
                InvocationTimeoutOverride = parsedOptions.InvocationTimeout,
                IsOneway = parsedOptions.IsOneway ?? false,
                Label = parsedOptions.Label,
                LocationResolver = communicator.DefaultLocationResolver,
                Path = uri.AbsolutePath,
                PreferExistingConnectionOverride = parsedOptions.PreferExistingConnection,
                PreferNonSecureOverride = parsedOptions.PreferNonSecure,
                Protocol = parsedOptions.Protocol ?? Protocol.Ice2
            };
        }

        /// <summary>Registers the ice and ice+universal schemes.</summary>
        internal static void RegisterCommon()
        {
            RegisterTransport("universal", UniversalEndpoint.DefaultUniversalPort);

            // There is actually no authority at all with the ice scheme, but we emulate it with an empty authority
            // during parsing by the Uri class and the GenericUriParser.
            GenericUriParserOptions options =
                ParserOptions |
                GenericUriParserOptions.AllowEmptyAuthority |
                GenericUriParserOptions.NoPort;

            System.UriParser.Register(new GenericUriParser(options), "ice", -1);
        }

        /// <summary>Registers an ice+transport scheme.</summary>
        /// <param name="transportName">The name of the transport (cannot be empty).</param>
        /// <param name="defaultPort">The default port for this transport.</param>
        internal static void RegisterTransport(string transportName, ushort defaultPort) =>
            System.UriParser.Register(new GenericUriParser(ParserOptions), $"ice+{transportName}", defaultPort);

        private static Endpoint CreateEndpoint(
            Communicator communicator,
            bool serverEndpoint,
            Dictionary<string, string> options,
            Protocol protocol,
            Uri uri)
        {
            Debug.Assert(uri.Scheme.StartsWith("ice+", StringComparison.InvariantCulture));
            string transportName = uri.Scheme[4..]; // i.e. chop-off "ice+"

            ushort port;
            checked
            {
                port = (ushort)uri.Port;
            }

            Ice2EndpointParser? parser = null;
            Transport transport;
            if (transportName == "universal")
            {
                if (serverEndpoint)
                {
                    throw new FormatException("ice+universal cannot specify a server endpoint");
                }

                // Enumerator names can only be used for "well-known" transports.
                transport = Enum.Parse<Transport>(options["transport"], ignoreCase: true);
                options.Remove("transport");

                if (protocol == Protocol.Ice2)
                {
                    // It's possible we have a factory for this transport, and we check it only when the protocol is
                    // ice2 (otherwise, we want to create a UniversalEndpoint).
                    parser = communicator.FindIce2EndpointParser(transport);
                }
            }
            else if (communicator.FindIce2EndpointParser(transportName) is (Ice2EndpointParser p, Transport t))
            {
                if (protocol != Protocol.Ice2)
                {
                    throw new FormatException(
                        $"cannot create an `{uri.Scheme}' endpoint for protocol `{protocol.GetName()}'");
                }
                parser = p;
                transport = t;
            }
            else
            {
                throw new FormatException($"unknown transport `{transportName}'");
            }

            // parser can be non-null only when the protocol is ice2.

            Endpoint endpoint = parser?.Invoke(transport,
                                               uri.DnsSafeHost,
                                               port,
                                               options,
                                               communicator,
                                               serverEndpoint) ??
                UniversalEndpoint.Parse(transport, uri.DnsSafeHost, port, options, communicator, protocol);

            if (options.Count > 0)
            {
                throw new FormatException($"unknown option `{options.First().Key}' for transport `{transportName}'");
            }
            return endpoint;
        }

        /// <summary>Creates a Uri and parses its query.</summary>
        /// <param name="uriString">The string to parse.</param>
        /// <param name="pureEndpoints">When true, the string represents one or more endpoints, and proxy options are
        /// not allowed in the query.</param>
        /// <param name="endpointOptions">A dictionary that accepts the parsed endpoint options. Set to null when
        /// parsing an ice URI (and in this case pureEndpoints must be false).</param>
        /// <returns>The parsed URI, the alt-endpoint option (if set) and the ProxyOptions struct.</returns>
        private static (Uri Uri, string? AltEndpoint, ParsedOptions ProxyOptions) InitialParse(
            string uriString,
            bool pureEndpoints,
            Dictionary<string, string>? endpointOptions)
        {
            if (endpointOptions == null) // i.e. ice scheme
            {
                Debug.Assert(uriString.StartsWith("ice:", StringComparison.InvariantCulture));
                Debug.Assert(!pureEndpoints);

                string body = uriString[4..];
                if (body.StartsWith("//", StringComparison.InvariantCulture))
                {
                    throw new FormatException("the ice URI scheme cannot define a host or port");
                }
                // Add empty authority for Uri's constructor.
                if (body.StartsWith('/'))
                {
                    uriString = $"ice://{body}";
                }
                else
                {
                    uriString = $"ice:///{body}";
                }
            }

            var uri = new Uri(uriString);

            if (pureEndpoints)
            {
                Debug.Assert(uri.AbsolutePath[0] == '/'); // there is always a first segment
                if (uri.AbsolutePath.Length > 1 || uri.Fragment.Length > 0)
                {
                    throw new FormatException($"endpoint `{uriString}' must not specify a path or fragment");
                }
            }

            string[] nvPairs = uri.Query.Length >= 2 ? uri.Query.TrimStart('?').Split('&') : Array.Empty<string>();

            string? altEndpoint = null;
            ParsedOptions proxyOptions = default;

            foreach (string p in nvPairs)
            {
                int equalPos = p.IndexOf('=');
                if (equalPos <= 0 || equalPos == p.Length - 1)
                {
                    throw new FormatException($"invalid option `{p}'");
                }
                string name = p[..equalPos];
                string value = p[(equalPos + 1)..];

                if (name == "context")
                {
                    if (pureEndpoints)
                    {
                        throw new FormatException($"{name} is not a valid option for endpoint `{uriString}'");
                    }

                    // We can have multiple context options: context=key1=value1,key2=value2 etc.
                    foreach (string e in value.Split(','))
                    {
                        equalPos = e.IndexOf('=');
                        if (equalPos <= 0)
                        {
                            throw new FormatException($"invalid option `{p}'");
                        }
                        string contextKey = Uri.UnescapeDataString(e[..equalPos]);
                        string contextValue =
                            equalPos == e.Length - 1 ? "" : Uri.UnescapeDataString(e[(equalPos + 1)..]);

                        proxyOptions.Context ??= new SortedDictionary<string, string>();
                        proxyOptions.Context[contextKey] = contextValue;
                    }
                }
                else if (name == "cache-connection")
                {
                    CheckProxyOption(name, proxyOptions.CacheConnection != null);
                    proxyOptions.CacheConnection = bool.Parse(value);
                }
                else if (name == "encoding")
                {
                    CheckProxyOption(name, proxyOptions.Encoding != null);
                    proxyOptions.Encoding = Encoding.Parse(value);
                }
                else if (name == "invocation-timeout")
                {
                    CheckProxyOption(name, proxyOptions.InvocationTimeout != null);
                    proxyOptions.InvocationTimeout = TimeSpanExtensions.Parse(value);
                    if (proxyOptions.InvocationTimeout.Value == TimeSpan.Zero)
                    {
                        throw new FormatException($"0 is not a valid value for the {name} option in `{uriString}'");
                    }
                }
                else if (name == "label")
                {
                    CheckProxyOption(name, proxyOptions.Label != null);
                    proxyOptions.Label = value;
                }
                else if (name == "oneway")
                {
                    CheckProxyOption(name, proxyOptions.IsOneway != null);
                    proxyOptions.IsOneway = bool.Parse(value);
                }
                else if (name == "prefer-existing-connection")
                {
                    CheckProxyOption(name, proxyOptions.PreferExistingConnection != null);
                    proxyOptions.PreferExistingConnection = bool.Parse(value);
                }
                else if (name == "prefer-non-secure")
                {
                    CheckProxyOption(name, proxyOptions.PreferNonSecure != null);
                    if (int.TryParse(value, out int _))
                    {
                        throw new FormatException($"{value} is not a valid option for prefer-non-secure");
                    }
                    proxyOptions.PreferNonSecure = Enum.Parse<NonSecure>(value, ignoreCase: true);
                }
                else if (name == "protocol")
                {
                    CheckProxyOption(name, proxyOptions.Protocol != null);
                    proxyOptions.Protocol = ProtocolExtensions.Parse(value);
                    if (proxyOptions.Protocol == Protocol.Ice1)
                    {
                        throw new FormatException("the URI format does not support protocol ice1");
                    }
                }
                else if (name == "fixed")
                {
                    throw new FormatException("cannot create or recreate a fixed proxy from a URI");
                }
                else if (endpointOptions == null)
                {
                    // We've parsed all known proxy options so the remaining options must be endpoint options or
                    // alt-endpoint, which applies only to a direct proxy.
                    throw new FormatException($"the ice URI scheme does not support option `{name}'");
                }
                else if (name == "alt-endpoint")
                {
                    altEndpoint = altEndpoint == null ? value : $"{altEndpoint},{value}";
                }
                else
                {
                    if (endpointOptions.TryGetValue(name, out string? existingValue))
                    {
                        endpointOptions[name] = $"{existingValue},{value}";
                    }
                    else
                    {
                        endpointOptions.Add(name, value);
                    }
                }
            }
            return (uri, altEndpoint, proxyOptions);

            void CheckProxyOption(string name, bool alreadySet)
            {
                if (pureEndpoints)
                {
                    throw new FormatException($"{name} is not a valid option for endpoint `{uriString}'");
                }
                if (alreadySet)
                {
                    throw new FormatException($"multiple {name} options in `{uriString}'");
                }
            }
        }

        /// <summary>Parses an ice or ice+transport URI string.</summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <param name="serverEndpoints">True when parsing the endpoints of a server; false when parsing a proxy.
        /// </param>
        /// <param name="communicator">The communicator.</param>
        /// <returns>The Uri and endpoints of the ice or ice+transport URI.</returns>
        private static (Uri Uri, IReadOnlyList<Endpoint> Endpoints, ParsedOptions ProxyOptions) Parse(
            string uriString,
            bool serverEndpoints,
            Communicator communicator)
        {
            Debug.Assert(IsProxyUri(uriString));

            try
            {
                bool iceScheme = uriString.StartsWith("ice:", StringComparison.InvariantCulture);
                if (iceScheme && serverEndpoints)
                {
                    throw new FormatException("a server endpoint supports only ice+transport URIs");
                }

                Dictionary<string, string>? endpointOptions = iceScheme ? null : new Dictionary<string, string>();

                (Uri uri, string? altEndpoint, ParsedOptions proxyOptions) =
                    InitialParse(uriString, pureEndpoints: serverEndpoints, endpointOptions);

                if (serverEndpoints && !IPAddress.TryParse(uri.Host, out IPAddress? address))
                {
                    throw new FormatException($"invalid IP address `{uri.Host}' in server endpoint `{uri}'");
                }

                Protocol protocol = proxyOptions.Protocol ?? Protocol.Ice2;

                List<Endpoint>? endpoints = null;

                if (endpointOptions != null) // i.e. not ice scheme
                {
                    endpoints = new List<Endpoint>
                    {
                        CreateEndpoint(communicator, serverEndpoints, endpointOptions, protocol, uri)
                    };

                    if (altEndpoint != null)
                    {
                        foreach (string endpointStr in altEndpoint.Split(','))
                        {
                            if (endpointStr.StartsWith("ice:", StringComparison.InvariantCulture))
                            {
                                throw new FormatException(
                                    $"invalid URI scheme for endpoint `{endpointStr}': must be empty or ice+transport");
                            }

                            string altUriString = endpointStr;
                            if (!altUriString.StartsWith("ice+", StringComparison.InvariantCulture))
                            {
                                altUriString = $"{uri.Scheme}://{altUriString}";
                            }

                            // The separator for endpoint options in alt-endpoint is $, and we replace these $ by &
                            // before sending the string the main parser (InitialParse), which uses & as separator.
                            altUriString = altUriString.Replace('$', '&');

                            // No need to clear endpointOptions before reusing it since CreateEndpoint consumes all the
                            // endpoint options
                            Debug.Assert(endpointOptions.Count == 0);

                            (Uri endpointUri, string? endpointAltEndpoint, _) =
                                InitialParse(altUriString, pureEndpoints: true, endpointOptions);

                            if (endpointAltEndpoint != null)
                            {
                                throw new FormatException(
                                    $"invalid option `alt-endpoint' in endpoint `{endpointStr}'");
                            }

                            endpoints.Add(CreateEndpoint(communicator,
                                                         serverEndpoints,
                                                         endpointOptions,
                                                         protocol,
                                                         endpointUri));
                        }
                    }
                }
                return (uri, (IReadOnlyList<Endpoint>?)endpoints ?? ImmutableArray<Endpoint>.Empty, proxyOptions);
            }
            catch (Exception ex)
            {
                // Give context to the exception.
                throw new FormatException($"failed to parse URI `{uriString}'", ex);
            }
        }
    }
}
