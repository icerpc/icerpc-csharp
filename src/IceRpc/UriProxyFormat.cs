// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace IceRpc
{
    /// <summary>The default proxy format with icerpc and icerpc+transport URIs.</summary>
    public class UriProxyFormat : IProxyFormat
    {
        /// <summary>The only instance of UriProxyFormat.</summary>
        public static IProxyFormat Instance { get; } = new UriProxyFormat();

        internal const ushort DefaultUriPort = 4062;

        private const string IceRpcColon = "icerpc:";
        private const string IceRpcPlus = "icerpc+";

        private static readonly object _mutex = new();

        /// <inheritdoc/>
        public Proxy Parse(string s, IInvoker? invoker = null)
        {
            string uriString = s.Trim();

            bool iceScheme = uriString.StartsWith(IceRpcColon, StringComparison.Ordinal);

            if (iceScheme)
            {
                string body = uriString[IceRpcColon.Length..]; // chop-off "icerpc:"
                if (body.StartsWith("//", StringComparison.Ordinal))
                {
                    throw new FormatException("the icerpc URI scheme does not support a host or port");
                }
                // Add empty authority for Uri's constructor.
                uriString = body.StartsWith('/') ? $"{IceRpcColon}//{body}" : $"{IceRpcColon}///{body}";

                TryAddScheme("ice");
            }
            else
            {
                if (!uriString.StartsWith(IceRpcPlus, StringComparison.Ordinal))
                {
                    throw new FormatException($"'{uriString}' is not a proxy URI");
                }

                string scheme = uriString[0..uriString.IndexOf(':', IceRpcPlus.Length)];
                if (scheme.Length == 0)
                {
                    throw new FormatException($"endpoint '{uriString}' does not specify a transport");
                }
                TryAddScheme(scheme);
            }

            var uri = new Uri(uriString);

            (ImmutableList<EndpointParam> endpointParams, Protocol? protocol, string? altEndpointValue, string? encoding) =
                ParseQuery(uri.Query, uriString);

            protocol ??= Protocol.IceRpc;

            Endpoint? endpoint = null;
            ImmutableList<Endpoint> altEndpoints = ImmutableList<Endpoint>.Empty;
            if (!iceScheme)
            {
                endpoint = CreateEndpoint(uri, endpointParams, protocol, uriString);

                if (altEndpointValue != null)
                {
                    // Split and parse recursively each endpoint
                    foreach (string endpointStr in altEndpointValue.Split(','))
                    {
                        string altUriString = endpointStr;
                        if (!altUriString.StartsWith(IceRpcColon, StringComparison.Ordinal) &&
                            !altUriString.Contains("://", StringComparison.Ordinal))
                        {
                            altUriString = $"{uri.Scheme}://{altUriString}";
                        }

                        // The separator for endpoint options in alt-endpoint is $, and we replace these $ by &
                        // before sending the string to ParseEndpointUri which uses & as separator.
                        altUriString = altUriString.Replace('$', '&');

                        Endpoint parsedEndpoint = ParseEndpoint(altUriString, endpoint.Protocol);

                        if (parsedEndpoint.Protocol != endpoint.Protocol)
                        {
                            throw new FormatException(
                                $"the protocol of all endpoints in '{uriString}' must be the same");
                        }
                        altEndpoints = altEndpoints.Add(parsedEndpoint);
                    }
                }
            }

            Debug.Assert(
                uri.AbsolutePath.Length > 0 &&
                uri.AbsolutePath[0] == '/' &&
                Proxy.IsValidPath(uri.AbsolutePath));

            return new Proxy(uri.AbsolutePath, protocol)
            {
                Invoker = invoker,
                Endpoint = endpoint,
                AltEndpoints = altEndpoints,
                Encoding = encoding == null ? (protocol.IceEncoding ?? Encoding.Unknown) : Encoding.FromString(encoding),
                Fragment = uri.Fragment.Length > 0 ? uri.Fragment[1..] : "" // remove #
            };
        }

        /// <inheritdoc/>
        public string ToString(Proxy proxy)
        {
            var sb = new StringBuilder();
            bool firstOption = true;

            if (proxy.Endpoint != null)
            {
                // Use icerpc+transport scheme
                sb.AppendEndpoint(proxy.Endpoint, proxy.Path);

                firstOption = proxy.Endpoint.Protocol == Protocol.IceRpc && proxy.Endpoint.Params.Count == 0;
            }
            else
            {
                sb.Append(IceRpcColon); // endpointless proxy
                sb.Append(proxy.Path);

                StartQueryOption(sb, ref firstOption);
                sb.Append("protocol=");
                sb.Append(proxy.Protocol);
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
                string mainTransport = proxy.Endpoint!.Transport;
                StartQueryOption(sb, ref firstOption);
                sb.Append("alt-endpoint=");
                for (int i = 0; i < proxy.AltEndpoints.Count; ++i)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.AppendEndpoint(proxy.AltEndpoints[i], "", mainTransport != proxy.AltEndpoints[i].Transport, '$');
                }
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

        /// <summary>Parses an icerpc+transport URI string that represents a single endpoint.</summary>
        /// <param name="uriString">The URI string to parse.</param>
        /// <param name="defaultProtocol">The default protocol.</param>
        /// <returns>The parsed endpoint.</returns>
        internal static Endpoint ParseEndpoint(string uriString, Protocol defaultProtocol)
        {
            if (!uriString.StartsWith(IceRpcPlus, StringComparison.Ordinal))
            {
                throw new FormatException($"endpoint '{uriString}' is not an {IceRpcPlus} URI");
            }

            string scheme = uriString[0..uriString.IndexOf(':', IceRpcPlus.Length)];
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
            return CreateEndpoint(uri, endpointParams, protocol ?? defaultProtocol, uriString);
        }

        private static Endpoint CreateEndpoint(
            Uri uri,
            ImmutableList<EndpointParam> endpointParams,
            Protocol protocol,
            string uriString) => new(protocol,
                                     uri.Scheme[IceRpcPlus.Length..],
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
                    protocol = protocol == null ? Protocol.Parse(value) :
                        throw new FormatException($"too many protocol query parameters in URI {uriString}");
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

        private UriProxyFormat()
        {
            // ensures it's a singleton
        }
    }
}
