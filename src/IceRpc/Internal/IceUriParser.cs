// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc.Internal
{
    /// <summary>Provides helper methods to parse ice and ice+transport URIs.</summary>
    internal static class IceUriParser
    {
        internal const ushort DefaultUriPort = 4062;

        private const string IceColon = "ice:";
        private const string IcePlus = "ice+";

        private static readonly object _mutex = new();

        /// <summary>Checks if a string is an ice+transport URI, and not an endpoint string using the ice1 string
        /// format.</summary>
        /// <param name="s">The string to check.</param>
        /// <returns>True when the string is most likely an ice+transport URI; otherwise, false.</returns>
        internal static bool IsEndpointUri(string s) =>
            s.StartsWith(IcePlus, StringComparison.Ordinal) && s.Contains("://", StringComparison.Ordinal);

        /// <summary>Checks if a string is an ice or ice+transport URI, and not a proxy string using the ice1 string
        /// format.</summary>
        /// <param name="s">The string to check.</param>
        /// <returns>True when the string is most likely an ice or ice+transport URI; otherwise, false.</returns>
        internal static bool IsProxyUri(string s) =>
            s.StartsWith(IceColon, StringComparison.Ordinal) || IsEndpointUri(s);

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

            // The printable ASCII character range is x20 (space) to x7E inclusive. And space is an invalid character
            // in addition to the invalid characters in the invalidChars string.
            foreach (char c in path)
            {
                if (c.CompareTo('\x20') <= 0 ||
                    c.CompareTo('\x7F') >= 0 ||
                    invalidChars.Contains(c, StringComparison.InvariantCulture))
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
        /// <param name="defaultProtocol">The default Ice protocol.</param>
        /// <returns>The parsed endpoint.</returns>
        internal static Endpoint ParseEndpointUri(string uriString, Protocol defaultProtocol = Protocol.Ice2)
        {
            Debug.Assert(uriString.StartsWith(IcePlus, StringComparison.Ordinal));

            string scheme = uriString[0..uriString.IndexOf(':', IcePlus.Length)];
            if (scheme.Length == 0)
            {
                throw new FormatException($"endpoint '{uriString}' does not specify a transport");
            }

            TryAddScheme(scheme);

            var uri = new Uri(uriString);

            (ImmutableList<EndpointParam> endpointParams, Protocol? protocol, string? altEndpoint, string? encoding) =
                ParseQuery(uri.Query, uriString);

            if (uri.AbsolutePath.Length > 1)
            {
                throw new FormatException($"invalid path in endpoint '{uriString}'");
            }
            if (altEndpoint != null)
            {
                throw new FormatException($"invalid alt-endpoint parameter in endpoint '{uriString}'");
            }
            if (encoding != null)
            {
                throw new FormatException($"invalid encoding parameter in endpoint '{uriString}'");
            }
            return CreateEndpoint(uri, endpointParams, protocol ?? defaultProtocol, uriString);
        }

        internal static Proxy ParseProxyUri(string uriString)
        {
            bool iceScheme = uriString.StartsWith(IceColon, StringComparison.Ordinal);

            if (iceScheme)
            {
                string body = uriString[IceColon.Length..]; // chop-off "ice:"
                if (body.StartsWith("//", StringComparison.Ordinal))
                {
                    throw new FormatException("the ice URI scheme does not support a host or port");
                }
                // Add empty authority for Uri's constructor.
                uriString = body.StartsWith('/') ? $"{IceColon}//{body}" : $"{IceColon}///{body}";

                TryAddScheme("ice");
            }
            else
            {
                string scheme = uriString[0..uriString.IndexOf(':', IcePlus.Length)];
                if (scheme.Length == 0)
                {
                    throw new FormatException($"endpoint '{uriString}' does not specify a transport");
                }
                TryAddScheme(scheme);
            }

            var uri = new Uri(uriString);

            (ImmutableList<EndpointParam> endpointParams, Protocol? protocol, string? altEndpointValue, string? encoding) =
                ParseQuery(uri.Query, uriString);

            protocol ??= Protocol.Ice2;

            Endpoint? endpoint = null;
            ImmutableList<Endpoint> altEndpoints = ImmutableList<Endpoint>.Empty;
            if (!iceScheme)
            {
                endpoint = CreateEndpoint(uri, endpointParams, protocol.Value, uriString);

                if (altEndpointValue != null)
                {
                    // Split and parse recursively each endpoint
                    foreach (string endpointStr in altEndpointValue.Split(','))
                    {
                        string altUriString = endpointStr;
                        if (!altUriString.StartsWith(IceColon, StringComparison.Ordinal) &&
                            !altUriString.Contains("://", StringComparison.Ordinal))
                        {
                            altUriString = $"{uri.Scheme}://{altUriString}";
                        }

                        // The separator for endpoint options in alt-endpoint is $, and we replace these $ by &
                        // before sending the string to ParseEndpointUri which uses & as separator.
                        altUriString = altUriString.Replace('$', '&');

                        Endpoint parsedEndpoint = ParseEndpointUri(altUriString, endpoint.Protocol);

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

            return new Proxy(uri.AbsolutePath, protocol.Value)
            {
                Endpoint = endpoint,
                AltEndpoints = altEndpoints,
                Encoding = encoding == null ? protocol.Value.GetEncoding() : Encoding.FromString(encoding)
            };
        }

        private static Endpoint CreateEndpoint(
            Uri uri,
            ImmutableList<EndpointParam> endpointParams,
            Protocol protocol,
            string uriString) => new(protocol,
                                     uri.Scheme[IcePlus.Length..],
                                     uri.DnsSafeHost,
                                     checked((ushort)uri.Port),
                                     endpointParams);

        private static (ImmutableList<EndpointParam> EndpointParams, Protocol? Protocol, string? AltEndpoint, string? Encoding) ParseQuery(
            string query,
            string uriString)
        {
            var endpointParams = new List<EndpointParam>();
            Protocol? protocol = null;
            string? altEndpoint = null;
            string? encoding = null;

            string[] nvPairs = query.Length >= 2 ? query.TrimStart('?').Split('&') : Array.Empty<string>();

            foreach (string p in nvPairs)
            {
                int equalPos = p.IndexOf('=', StringComparison.Ordinal);
                if (equalPos <= 0)
                {
                    throw new FormatException($"invalid query parameter '{p}' in URI {uriString}");
                }
                string name = p[..equalPos];
                string value = p[(equalPos + 1)..];

                if (name == "alt-endpoint")
                {
                    altEndpoint = altEndpoint == null ? value : $"{altEndpoint},{value}";
                }
                else if (name == "encoding")
                {
                    encoding = encoding == null ? value :
                        throw new FormatException($"too many encoding query parameters in URI {uriString}");
                }
                else if (name == "protocol")
                {
                    protocol = protocol == null ? ProtocolParser.Parse(value) :
                        throw new FormatException($"too many protocol query parameters in URI {uriString}");

                    if (protocol.Value == Protocol.Ice1)
                    {
                        throw new FormatException($"invalid protocol value in URI {uriString}");
                    }
                }
                else
                {
                    endpointParams.Add(new EndpointParam(name, value));
                }
            }
            return (endpointParams.ToImmutableList(), protocol, altEndpoint, encoding);
        }

        private static void TryAddScheme(string scheme)
        {
            lock (_mutex)
            {
                if (!UriParser.IsKnownScheme(scheme))
                {
                    GenericUriParserOptions parserOptions =
                        GenericUriParserOptions.DontUnescapePathDotsAndSlashes |
                        GenericUriParserOptions.Idn |
                        GenericUriParserOptions.IriParsing |
                        GenericUriParserOptions.NoFragment |
                        GenericUriParserOptions.NoUserInfo;

                    int defaultPort = DefaultUriPort;

                    if (scheme == "ice")
                    {
                        parserOptions |= GenericUriParserOptions.AllowEmptyAuthority | GenericUriParserOptions.NoPort;
                        defaultPort = -1;
                    }

                    // UriParser.Register requires a separate UriParser instance per scheme.
                    UriParser.Register(new GenericUriParser(parserOptions), scheme, defaultPort);
                }
            }
        }
    }
}
