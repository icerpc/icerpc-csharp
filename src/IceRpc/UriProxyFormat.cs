// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace IceRpc
{
    /// <summary>The default proxy format using URIs.</summary>
    public class UriProxyFormat : IProxyFormat
    {
        /// <summary>The only instance of UriProxyFormat.</summary>
        public static IProxyFormat Instance { get; } = new UriProxyFormat();

        internal const ushort DefaultUriPort = 4062;

        private static readonly object _mutex = new();

        /// <inheritdoc/>
        public Proxy Parse(string s, IInvoker? invoker = null)
        {
            string uriString = s.Trim();

            int colonIndex = uriString.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex == -1)
            {
                throw new FormatException($"'{uriString}' is not a valid URI");
            }

            string schemeName = uriString[0..colonIndex];

            // Uri.CheckSchemeName accepts 1-character long protocol name, but UriParser.Register does not.
            if (schemeName.Length < 2 || !Uri.CheckSchemeName(schemeName))
            {
                throw new FormatException($"'{uriString}' is not a valid URI");
            }

            var protocol = Protocol.FromString(schemeName);
            TryRegisterParser(schemeName);

            // If there is no authority in the string, we unfortunately need to add an explicit empty authority.
            string body = uriString[(protocol.Name.Length + 1)..]; // chop-off "protocol:"
            if (!body.StartsWith("//", StringComparison.Ordinal))
            {
                uriString = body.StartsWith('/') ? $"{protocol}://{body}" : $"{protocol}:///{body}";
            }

            var uri = new Uri(uriString);

            (ImmutableDictionary<string, string> queryParams, string? altEndpointValue, string? encoding) =
                ParseQuery(uri.Query, uriString);

            Endpoint? endpoint = null;
            ImmutableList<Endpoint> altEndpoints = ImmutableList<Endpoint>.Empty;
            if (uri.Authority.Length > 0)
            {
                endpoint = CreateEndpoint(uri, queryParams, protocol, uriString);

                if (altEndpointValue != null)
                {
                    // Split and parse recursively each endpoint
                    foreach (string endpointStr in altEndpointValue.Split(','))
                    {
                        string altUriString = $"{uri.Scheme}://{endpointStr}";

                        // The separator for endpoint options in alt-endpoint is $, and we replace these $ by &
                        // before sending the string to ParseEndpointUri which uses & as separator.
                        altUriString = altUriString.Replace('$', '&');
                        altEndpoints = altEndpoints.Add(ParseEndpoint(altUriString));
                    }
                }
            }

            Debug.Assert(
                uri.AbsolutePath.Length > 0 &&
                uri.AbsolutePath[0] == '/' &&
                Proxy.IsValidPath(uri.AbsolutePath));

            return new Proxy(uri.AbsolutePath, protocol)
            {
                Invoker = invoker ?? Proxy.DefaultInvoker,
                Endpoint = endpoint,
                AltEndpoints = altEndpoints,
                Encoding = encoding == null ?
                    (protocol.SliceEncoding ?? Encoding.Unknown) : Encoding.FromString(encoding),
                Fragment = uri.Fragment.Length > 0 ? uri.Fragment[1..] : "", // remove #
                Params = endpoint == null ? queryParams : ImmutableDictionary<string, string>.Empty
            };
        }

        /// <inheritdoc/>
        public string ToString(Proxy proxy)
        {
            var sb = new StringBuilder();
            bool firstOption = true;

            if (proxy.Endpoint != null)
            {
                sb.AppendEndpoint(proxy.Endpoint, proxy.Path);
                firstOption = proxy.Endpoint.Params.Count == 0;
            }
            else
            {
                sb.Append(proxy.Protocol);
                sb.Append(':');
                sb.Append(proxy.Path);
            }

            // TODO: remove
            if (proxy.Encoding != IceRpcDefinitions.Encoding)
            {
                StartQueryOption(sb, ref firstOption);
                sb.Append("encoding=");
                sb.Append(proxy.Encoding);
            }

            if (proxy.AltEndpoints.Count > 0)
            {
                StartQueryOption(sb, ref firstOption);
                sb.Append("alt-endpoint=");
                for (int i = 0; i < proxy.AltEndpoints.Count; ++i)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.AppendEndpoint(proxy.AltEndpoints[i], path: "", includeScheme: false, paramSeparator: '$');
                }
            }

            foreach ((string name, string value) in proxy.Params)
            {
                StartQueryOption(sb, ref firstOption);
                sb.Append(name);
                sb.Append('=');
                sb.Append(value);
            }

            if (proxy.Fragment.Length > 0)
            {
                sb.Append('#');
                sb.Append(proxy.Fragment);
            }

            return sb.ToString();

            static void StartQueryOption(StringBuilder sb, ref bool firstOption)
            {
                if (firstOption)
                {
                    sb.Append('?');
                    firstOption = false;
                }
                else
                {
                    sb.Append('&');
                }
            }
        }

        /// <summary>Parses a URI string that represents a single endpoint.</summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <returns>The parsed endpoint.</returns>
        internal static Endpoint ParseEndpoint(string uriString)
        {
            int colonIndex = uriString.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex == -1)
            {
                throw new FormatException($"'{uriString}' is not a valid URI");
            }

            string schemeName = uriString[0..colonIndex];

            // Uri.CheckSchemeName accepts 1-character long protocol name, but UriParser.Register does not.
            if (schemeName.Length < 2 || !Uri.CheckSchemeName(schemeName))
            {
                throw new FormatException($"'{uriString}' is not a valid URI");
            }

            var protocol = Protocol.FromString(schemeName);
            TryRegisterParser(schemeName);

            var uri = new Uri(uriString);

            (ImmutableDictionary<string, string> endpointParams, string? altEndpoint, string? encoding) =
                ParseQuery(uri.Query, uriString);

            if (uri.AbsolutePath.Length > 1)
            {
                throw new FormatException($"invalid path in endpoint '{uriString}'");
            }
            if (uri.Fragment.Length > 0)
            {
                throw new FormatException($"invalid fragment in endpoint '{uriString}'");
            }
            if (altEndpoint != null)
            {
                throw new FormatException($"invalid alt-endpoint parameter in endpoint '{uriString}'");
            }
            if (encoding != null)
            {
                throw new FormatException($"invalid encoding parameter in endpoint '{uriString}'");
            }
            return CreateEndpoint(uri, endpointParams, protocol, uriString);
        }

        private static Endpoint CreateEndpoint(
            Uri uri,
            ImmutableDictionary<string, string> endpointParams,
            Protocol protocol,
            string uriString)
        {
            string host = uri.DnsSafeHost;
            if (host.Length == 0)
            {
                throw new FormatException($"missing authority in endpoint URI '{uriString}'");
            }

            return new(protocol, host, checked((ushort)uri.Port), endpointParams);
        }

        private static (ImmutableDictionary<string, string> QueryParams, string? AltEndpoint, string? Encoding) ParseQuery(
            string query,
            string uriString)
        {
            var queryParams = new Dictionary<string, string>();
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
                else if (queryParams.TryGetValue(name, out string? existingValue))
                {
                    queryParams[name] = $"{existingValue},{value}";
                }
                else
                {
                    queryParams.Add(name, value);
                }
            }
            return (queryParams.ToImmutableDictionary(), altEndpoint, encoding);
        }

        private static void TryRegisterParser(string schemeName)
        {
            lock (_mutex)
            {
                if (!UriParser.IsKnownScheme(schemeName))
                {
                    // Unfortunately there is no way to specify the authority is optional. AllowEmptyAuthority means
                    // it can be empty, but must still be specified.
                    GenericUriParserOptions parserOptions =
                        GenericUriParserOptions.AllowEmptyAuthority |
                        GenericUriParserOptions.DontUnescapePathDotsAndSlashes |
                        GenericUriParserOptions.Idn |
                        GenericUriParserOptions.IriParsing |
                        GenericUriParserOptions.NoUserInfo;

                    // UriParser.Register requires a separate UriParser instance per protocol.
                    UriParser.Register(new GenericUriParser(parserOptions), schemeName, DefaultUriPort);
                }
            }
        }

        private UriProxyFormat()
        {
            // ensures it's a singleton
        }
    }
}
