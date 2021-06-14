// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace IceRpc.Internal
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
        /// <returns>The parsed endpoint.</returns>
        internal static Endpoint ParseEndpoint(string uriString)
        {
            Debug.Assert(uriString.StartsWith("ice+", StringComparison.Ordinal));

            Runtime.UriInitialize();
            var uri = new Uri(uriString);
            if (!System.UriParser.IsKnownScheme(uri.Scheme))
            {
                throw new FormatException($"the URI scheme of endpoint '{uriString}' is not registered");
            }
            if (uri.AbsolutePath.Length > 1)
            {
                throw new FormatException($"endpoint '{uriString}' cannot define a path");
            }

            ParsedOptions parsedOptions = ParseQuery(uri.Query, parseProxy: false, uriString);
            parsedOptions.EndpointOptions ??= new Dictionary<string, string>();

            return CreateEndpoint(parsedOptions.EndpointOptions, uri);
        }

        /// <summary>Parses an ice or ice+transport URI string that represents a proxy.</summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <returns>The arguments to create a proxy.</returns>
        internal static (string Path, Encoding Encoding, Endpoint? Endpoint, ImmutableList<Endpoint> AltEndpoints) ParseProxy(
            string uriString)
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
            if (!System.UriParser.IsKnownScheme(uri.Scheme))
            {
                throw new FormatException($"the URI scheme of proxy '{uriString}' is not registered");
            }

            ParsedOptions parsedOptions = ParseQuery(uri.Query, parseProxy: true, uriString);

            Endpoint? endpoint = null;
            var altEndpoints = ImmutableList<Endpoint>.Empty;
            if (!iceScheme)
            {
                parsedOptions.EndpointOptions ??= new Dictionary<string, string>();
                endpoint = CreateEndpoint(parsedOptions.EndpointOptions, uri);

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

                        Endpoint parsedEndpoint = ParseEndpoint(altUriString);

                        if (parsedEndpoint.Protocol != endpoint.Protocol)
                        {
                            throw new FormatException(
                                $"the protocol of all endpoints in '{uriString}' must be the same");
                        }
                        altEndpoints = altEndpoints.Add(parsedEndpoint);
                    }
                }
            }

            Debug.Assert(uri.AbsolutePath.Length > 0 && uri.AbsolutePath[0] == '/' && IsValidPath(uri.AbsolutePath));

            return (uri.AbsolutePath,
                    parsedOptions.Encoding ?? Encoding.V20,
                    endpoint,
                    altEndpoints);
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

        private static Endpoint CreateEndpoint(Dictionary<string, string> options, Uri uri)
        {
            Debug.Assert(uri.Scheme.StartsWith("ice+", StringComparison.Ordinal));
            string transportName = uri.Scheme[4..]; // i.e. chop-off "ice+"

            ushort port;
            checked
            {
                port = (ushort)uri.Port;
            }

            Func<string, ushort, Dictionary<string, string>, Endpoint>? parser = null;
            Transport transport = default;
            Endpoint? endpoint = null;

            if (transportName == "universal")
            {
                endpoint = UniversalEndpoint.Parse(uri.DnsSafeHost, port, options);

                if (endpoint.Protocol == Protocol.Ice2)
                {
                    // It's possible we have a factory for this transport, and we check it only when the protocol is
                    // ice2 (otherwise, we return this UniversalEndpoint).
                    // Since all options have been consumed by Parse above, this works only for endpoints with no
                    // options.
                    if (TransportRegistry.TryGetValue(endpoint.Transport, out TransportDescriptor? descriptor))
                    {
                        parser = descriptor.Ice2EndpointParser;
                    }
                    transport = endpoint.Transport;
                }
            }
            else if (TransportRegistry.TryGetValue(transportName, out var value))
            {
                parser = value.Descriptor.Ice2EndpointParser;
                transport = value.Transport;
            }
            else
            {
                throw new FormatException($"unknown transport '{transportName}'");
            }

            if (parser != null)
            {
                endpoint = parser(uri.DnsSafeHost, port, options);
            }
            else
            {
                endpoint ??= UniversalEndpoint.Parse(uri.DnsSafeHost, port, options);
            }

            if (options.Count > 0)
            {
                throw new FormatException($"unknown option '{options.First().Key}' for transport '{transportName}'");
            }
            return endpoint;
        }

        private static ParsedOptions ParseQuery(string query, bool parseProxy, string uriString)
        {
            bool iceScheme = uriString.StartsWith("ice:", StringComparison.Ordinal);

            string[] nvPairs = query.Length >= 2 ? query.TrimStart('?').Split('&') : Array.Empty<string>();

            ParsedOptions parsedOptions = default;

            foreach (string p in nvPairs)
            {
                int equalPos = p.IndexOf('=');
                if (equalPos <= 0 || equalPos == p.Length - 1)
                {
                    throw new FormatException($"invalid option '{p}'");
                }
                string name = p[..equalPos];
                string value = p[(equalPos + 1)..];

                if (name == "encoding")
                {
                    if (!parseProxy)
                    {
                        throw new FormatException($"{name} is not a valid option for endpoint '{uriString}'");
                    }
                    if (parsedOptions.Encoding != null)
                    {
                        throw new FormatException($"multiple {name} options in '{uriString}'");
                    }
                    parsedOptions.Encoding = Encoding.Parse(value);
                }
                else if (iceScheme)
                {
                    // We've parsed all known proxy options so the remaining options must be endpoint options or
                    // alt-endpoint, which apply only to ice+transport schemes.
                    throw new FormatException($"the ice URI scheme does not support option '{name}'");
                }
                else if (name == "alt-endpoint")
                {
                    if (!parseProxy)
                    {
                        throw new FormatException($"{name} is not a valid option for endpoint '{uriString}'");
                    }

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
        }

        /// <summary>The proxy and endpoint options parsed by the UriParser.</summary>
        private struct ParsedOptions
        {
            internal string? AltEndpoint;

            internal Encoding? Encoding;

            internal Dictionary<string, string>? EndpointOptions;
        }
    }
}
