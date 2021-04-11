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
            s.StartsWith("ice+", StringComparison.Ordinal) && s.Contains("://");

        /// <summary>Checks if a string is an ice or ice+transport URI, and not a proxy string using the ice1 string
        /// format.</summary>
        /// <param name="s">The string to check.</param>
        /// <returns>True when the string is most likely an ice or ice+transport URI; otherwise, false.</returns>
        internal static bool IsProxyUri(string s) =>
            s.StartsWith("ice:", StringComparison.Ordinal) || IsEndpointUri(s);

        /// <summary>Checks if <c>path</c> starts with <c>/</c> and contains only unreserved characters, <c>%</c>, or
        /// reserved characters other than <c>?</c>.</summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if <c>path</c> is a valid path; otherwise, false.</returns>
        internal static bool IsValidPath(string path)
        {
            const string invalidChars = "\"<>?\\^`{|}";

            if (path.Length == 0 || path[0] != '/')
            {
                return false;
            }

            foreach (char c in path)
            {
                if (c.CompareTo('\x20') <= 0 || c.CompareTo('\x7F') >= 0 || invalidChars.Contains(c))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Makes sure path is valid and throws ArgumentException if it is not.</summary>
        internal static void CheckPath(string path, string paramName)
        {
            if (!IsValidPath(path))
            {
                throw new ArgumentException(
                    @$"invalid path '{path
                    }'; a valid path starts with '/' and contains only unreserved characters, '%' or reserved characters other than '?'",
                    paramName);
            }
        }

        /// <summary>Parses an ice+transport URI string that represents a single endpoint.</summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <param name="protocol">The protocol of this endpoint, if known.</param>
        /// <returns>The parsed endpoint.</returns>
        internal static Endpoint ParseEndpoint(string uriString, Protocol? protocol = null)
        {
            Debug.Assert(uriString.StartsWith("ice+", StringComparison.Ordinal));

            Runtime.UriInitialize();
            var uri = new Uri(uriString);
            if (uri.AbsolutePath.Length > 1)
            {
                throw new FormatException($"endpoint '{uriString}' cannot define a path");
            }

            ParsedOptions parsedOptions = ParseQuery(uri.Query, parseProxy: false, protocol, uriString);
            parsedOptions.EndpointOptions ??= new Dictionary<string, string>();
            parsedOptions.Protocol ??= Protocol.Ice2;

            return CreateEndpoint(parsedOptions.EndpointOptions, parsedOptions.Protocol.Value, uri);
        }

        /// <summary>Parses an ice or ice+transport URI string that represents a proxy.</summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <param name="proxyOptions">The proxyOptions to set options that are not parsed.</param>
        /// <returns>The arguments to create a proxy.</returns>
        internal static (string Path, Protocol Protocol, Encoding Encoding, IEnumerable<Endpoint> Endpoints, ProxyOptions Options) ParseProxy(
            string uriString,
            ProxyOptions proxyOptions)
        {
            bool iceScheme = uriString.StartsWith("ice:", StringComparison.Ordinal);

            if (iceScheme)
            {
                string body = uriString[4..]; // chop-off "ice:"
                if (body.StartsWith("//", StringComparison.Ordinal))
                {
                    throw new FormatException("the ice URI scheme does not support a host or port");
                }
                // Add empty authority for Uri's constructor.
                uriString = body.StartsWith('/') ? $"ice://{body}" : $"ice:///{body}";
            }

            Runtime.UriInitialize();
            var uri = new Uri(uriString);

            ParsedOptions parsedOptions = ParseQuery(uri.Query, parseProxy: true, protocol: null, uriString);
            parsedOptions.Protocol ??= Protocol.Ice2;

            ImmutableList<Endpoint> endpoints = ImmutableList<Endpoint>.Empty;
            if (!iceScheme)
            {
                parsedOptions.EndpointOptions ??= new Dictionary<string, string>();
                endpoints = ImmutableList.Create(
                    CreateEndpoint(parsedOptions.EndpointOptions, parsedOptions.Protocol.Value, uri));

                if (parsedOptions.AltEndpoint is string altEndpoint)
                {
                    // Split and parse recursively each endpoint
                    foreach (string endpointStr in altEndpoint.Split(','))
                    {
                        if (endpointStr.StartsWith("ice:", StringComparison.Ordinal))
                        {
                            throw new FormatException(
                                $"invalid URI scheme for endpoint '{endpointStr}': must be empty or ice+transport");
                        }

                        string altUriString = endpointStr;
                        if (!altUriString.StartsWith("ice+", StringComparison.Ordinal))
                        {
                            altUriString = $"{uri.Scheme}://{altUriString}";
                        }

                        // The separator for endpoint options in alt-endpoint is $, and we replace these $ by &
                        // before sending the string to ParseEndpoint which uses & as separator.
                        altUriString = altUriString.Replace('$', '&');

                        endpoints = endpoints.Add(ParseEndpoint(altUriString, parsedOptions.Protocol));
                    }
                }
            }

            Debug.Assert(uri.AbsolutePath.Length > 0 && uri.AbsolutePath[0] == '/' && IsValidPath(uri.AbsolutePath));

            proxyOptions = proxyOptions.Clone();

            proxyOptions.CacheConnection = parsedOptions.CacheConnection ?? proxyOptions.CacheConnection;
            proxyOptions.Context = parsedOptions.Context?.ToImmutableSortedDictionary() ?? proxyOptions.Context;
            proxyOptions.IsOneway = parsedOptions.IsOneway ?? proxyOptions.IsOneway;
            proxyOptions.InvocationTimeout = parsedOptions.InvocationTimeout ?? proxyOptions.InvocationTimeout;
            proxyOptions.PreferExistingConnection =
                parsedOptions.PreferExistingConnection ?? proxyOptions.PreferExistingConnection;
            proxyOptions.NonSecure = parsedOptions.NonSecure ?? proxyOptions.NonSecure;

            return (uri.AbsolutePath,
                    parsedOptions.Protocol.Value,
                    parsedOptions.Encoding ?? Encoding.V20,
                    endpoints,
                    proxyOptions);
        }

        /// <summary>Registers the ice and ice+universal schemes.</summary>
        internal static void RegisterIceScheme()
        {
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
            Dictionary<string, string> options,
            Protocol protocol,
            Uri uri)
        {
            Debug.Assert(uri.Scheme.StartsWith("ice+", StringComparison.Ordinal));
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
                if (options.TryGetValue("transport", out string? actualTransportName))
                {
                    // Enumerator names can only be used for "well-known" transports.
                    transport = Enum.Parse<Transport>(actualTransportName, ignoreCase: true);
                    options.Remove("transport");

                    if (protocol == Protocol.Ice2)
                    {
                        // It's possible we have a factory for this transport, and we check it only when the protocol is
                        // ice2 (otherwise, we want to create a UniversalEndpoint).
                        parser = Runtime.FindIce2EndpointParser(transport);
                    }
                }
                else
                {
                    throw new FormatException("ice+universal endpoint does not have required 'transport' option");
                }
            }
            else if (Runtime.FindIce2EndpointParser(transportName) is (Ice2EndpointParser p, Transport t))
            {
                if (protocol != Protocol.Ice2)
                {
                    throw new FormatException(
                        $"cannot create an '{uri.Scheme}' endpoint for protocol '{protocol.GetName()}'");
                }
                parser = p;
                transport = t;
            }
            else
            {
                throw new FormatException($"unknown transport '{transportName}'");
            }

            // parser can be non-null only when the protocol is ice2.
            Endpoint endpoint = parser?.Invoke(transport,
                                               uri.DnsSafeHost,
                                               port,
                                               options) ??
                UniversalEndpoint.Parse(transport, uri.DnsSafeHost, port, options, protocol);

            if (options.Count > 0)
            {
                throw new FormatException($"unknown option '{options.First().Key}' for transport '{transportName}'");
            }
            return endpoint;
        }

        private static ParsedOptions ParseQuery(string query, bool parseProxy, Protocol? protocol, string uriString)
        {
            bool iceScheme = uriString.StartsWith("ice:", StringComparison.Ordinal);

            string[] nvPairs = query.Length >= 2 ? query.TrimStart('?').Split('&') : Array.Empty<string>();

            ParsedOptions parsedOptions = default;
            parsedOptions.Protocol = protocol; // when parsing the endpoint of a proxy, the protocol cannot change

            foreach (string p in nvPairs)
            {
                int equalPos = p.IndexOf('=');
                if (equalPos <= 0 || equalPos == p.Length - 1)
                {
                    throw new FormatException($"invalid option '{p}'");
                }
                string name = p[..equalPos];
                string value = p[(equalPos + 1)..];

                if (name == "context")
                {
                    if (!parseProxy)
                    {
                        throw new FormatException($"{name} is not a valid option for endpoint '{uriString}'");
                    }

                    // We can have multiple context options: context=key1=value1,key2=value2 etc.
                    foreach (string e in value.Split(','))
                    {
                        equalPos = e.IndexOf('=');
                        if (equalPos <= 0)
                        {
                            throw new FormatException($"invalid option '{p}'");
                        }
                        string contextKey = Uri.UnescapeDataString(e[..equalPos]);
                        string contextValue =
                            equalPos == e.Length - 1 ? "" : Uri.UnescapeDataString(e[(equalPos + 1)..]);

                        parsedOptions.Context ??= new SortedDictionary<string, string>();
                        parsedOptions.Context[contextKey] = contextValue;
                    }
                }
                else if (name == "cache-connection")
                {
                    CheckProxyOption(name, parsedOptions.CacheConnection != null);
                    parsedOptions.CacheConnection = bool.Parse(value);
                }
                else if (name == "encoding")
                {
                    CheckProxyOption(name, parsedOptions.Encoding != null);
                    parsedOptions.Encoding = Encoding.Parse(value);
                }
                else if (name == "invocation-timeout")
                {
                    CheckProxyOption(name, parsedOptions.InvocationTimeout != null);
                    parsedOptions.InvocationTimeout = TimeSpanExtensions.Parse(value);
                    if (parsedOptions.InvocationTimeout.Value == TimeSpan.Zero)
                    {
                        throw new FormatException($"0 is not a valid value for the {name} option in '{uriString}'");
                    }
                }
                else if (name == "non-secure")
                {
                    CheckProxyOption(name, parsedOptions.NonSecure != null);
                    if (int.TryParse(value, out int _))
                    {
                        throw new FormatException($"{value} is not a valid option for non-secure");
                    }
                    parsedOptions.NonSecure = Enum.Parse<NonSecure>(value, ignoreCase: true);
                }
                else if (name == "oneway")
                {
                    CheckProxyOption(name, parsedOptions.IsOneway != null);
                    parsedOptions.IsOneway = bool.Parse(value);
                }
                else if (name == "prefer-existing-connection")
                {
                    CheckProxyOption(name, parsedOptions.PreferExistingConnection != null);
                    parsedOptions.PreferExistingConnection = bool.Parse(value);
                }
                else if (name == "protocol")
                {
                    // protocol is a valid option for a proxy URI and an endpoint URI/
                    if (parsedOptions.Protocol != null)
                    {
                        throw new FormatException($"multiple {name} options in '{uriString}'");
                    }
                    parsedOptions.Protocol = ProtocolExtensions.Parse(value);
                    if (parsedOptions.Protocol == Protocol.Ice1)
                    {
                        throw new FormatException("the URI format does not support protocol ice1");
                    }
                }
                else if (name == "fixed")
                {
                    throw new FormatException("cannot create or recreate a fixed proxy from a URI");
                }
                else if (iceScheme)
                {
                    // We've parsed all known proxy options so the remaining options must be endpoint options or
                    // alt-endpoint, which apply only to ice+transport schemes.
                    throw new FormatException($"the ice URI scheme does not support option '{name}'");
                }
                else if (name == "alt-endpoint")
                {
                    parsedOptions.AltEndpoint =
                        parsedOptions.AltEndpoint == null ? value : $"{parsedOptions.AltEndpoint},{value}";
                }
                else
                {
                    parsedOptions.EndpointOptions ??= new();

                    if (parsedOptions.EndpointOptions.TryGetValue(name, out string? existingValue))
                    {
                        parsedOptions.EndpointOptions[name] = $"{existingValue},{value}";
                    }
                    else
                    {
                        parsedOptions.EndpointOptions.Add(name, value);
                    }
                }
            }
            return parsedOptions;

            void CheckProxyOption(string name, bool alreadySet)
            {
                if (!parseProxy)
                {
                    throw new FormatException($"{name} is not a valid option for endpoint '{uriString}'");
                }
                if (alreadySet)
                {
                    throw new FormatException($"multiple {name} options in '{uriString}'");
                }
            }
        }

        /// <summary>The proxy and endpoint options parsed by the UriParser.</summary>
        private struct ParsedOptions
        {
            internal string? AltEndpoint;
            internal bool? CacheConnection;

            internal SortedDictionary<string, string>? Context;

            internal Encoding? Encoding;

            internal Dictionary<string, string>? EndpointOptions;

            internal TimeSpan? InvocationTimeout;

            internal bool? IsOneway;
            internal NonSecure? NonSecure;
            internal bool? PreferExistingConnection;
            internal Protocol? Protocol;
        }
    }
}
