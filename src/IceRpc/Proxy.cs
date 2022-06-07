// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc
{
    /// <summary>A proxy is a local ambassador for a remote IceRPC service and is used to create
    /// <see cref="OutgoingRequest"/> to this remote service. This Proxy class can be used with any encoding. The Slice
    /// proxies generated by the Slice compiler are typed structures (Prx structs) that wrap this Proxy: each Prx struct
    /// holds only a Proxy.</summary>
    /// <seealso cref="Slice.IPrx"/>
    public sealed record class Proxy
    {
        /// <summary>Gets the default invoker of proxies.</summary>
        public static IInvoker DefaultInvoker { get; } =
            new InlineInvoker((request, cancel) =>
                request.Connection?.InvokeAsync(request, cancel) ??
                    throw new ArgumentNullException(nameof(request), $"{nameof(request.Connection)} is null"));

        /// <summary>Gets or sets the secondary endpoints of this proxy.</summary>
        /// <value>The secondary endpoints of this proxy.</value>
        public ImmutableList<Endpoint> AltEndpoints
        {
            get => _altEndpoints;

            set
            {
                CheckSupportedProtocol(nameof(AltEndpoints));

                if (value.Count > 0)
                {
                    if (_endpoint == null)
                    {
                        throw new InvalidOperationException(
                            $"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)} is empty");
                    }

                    if (value.Any(e => e.Protocol != Protocol))
                    {
                        throw new ArgumentException(
                            $"the {nameof(AltEndpoints)} endpoints must use the proxy's protocol {Protocol}",
                            nameof(value));
                    }
                }
                // else, no need to check anything, an empty list is always fine.

                _altEndpoints = value;
                OriginalUri = null;
            }
        }

        /// <summary>Gets or sets the connection of this proxy. Setting the connection does not affect the proxy
        /// endpoints (if any).</summary>
        /// <value>The connection for this proxy, or null if the proxy does not have a connection.</value>
        public IConnection? Connection
        {
            get => _connection;

            set
            {
                CheckSupportedProtocol(nameof(Connection));
                if (value?.Protocol is Protocol newProtocol && newProtocol != Protocol)
                {
                    throw new ArgumentException(
                        $"the {nameof(Connection)} protocol must match the proxy's protocol '{Protocol}'",
                        nameof(value));
                }
                _connection = value;
            }
        }

        /// <summary>Gets or sets the main endpoint of this proxy.</summary>
        /// <value>The main endpoint of this proxy, or null if this proxy has no endpoint.</value>
        public Endpoint? Endpoint
        {
            get => _endpoint;

            set
            {
                CheckSupportedProtocol(nameof(Endpoint));
                if (value?.Protocol is Protocol newProtocol && newProtocol != Protocol)
                {
                    throw new ArgumentException(
                        $"the {nameof(Endpoint)} must use the proxy's protocol: '{Protocol}'",
                        nameof(value));
                }

                if (value != null)
                {
                    if (_params.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"cannot set {nameof(Endpoint)} on a proxy with parameters");
                    }
                }
                else if (_altEndpoints.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"cannot clear {nameof(Endpoint)} when {nameof(AltEndpoints)} is not empty");
                }
                _endpoint = value;
                OriginalUri = null;
            }
        }

        /// <summary>Gets or initializes the fragment.</summary>
        public string Fragment
        {
            get => _fragment;
            init
            {
                CheckSupportedProtocol(nameof(Fragment));

                try
                {
                    CheckFragment(value); // make sure it's properly escaped
                }
                catch (FormatException ex)
                {
                    throw new ArgumentException($"invalid fragment", nameof(Fragment), ex);
                }

                if (!Protocol.HasFragment && value.Length > 0)
                {
                    throw new InvalidOperationException($"cannot set {Fragment} on an {Protocol} proxy");
                }

                _fragment = value;
                OriginalUri = null;
            }
        }

        /// <summary>Gets or sets the invoker of this proxy.</summary>
        public IInvoker Invoker
        {
            get => _invoker;
            set
            {
                CheckSupportedProtocol(nameof(Invoker));
                _invoker = value;
            }
        }

        /// <summary>Gets the URI used to create this proxy, if this proxy was created from a URI and URI-derived
        /// properties such as <see cref="Endpoint"/> have not been updated.</summary>
        public Uri? OriginalUri { get; private set; }

        /// <summary>Gets or initializes the path of this proxy.</summary>
        public string Path
        {
            get => _path;
            init
            {
                if (Protocol == Protocol.Relative || Protocol.IsSupported)
                {
                    try
                    {
                        CheckPath(value); // make sure it's properly escaped
                        Protocol.CheckPath(value); // make sure the protocol is happy with this path
                    }
                    catch (FormatException ex)
                    {
                        throw new ArgumentException($"invalid path", nameof(Path), ex);
                    }
                    _path = value;
                    OriginalUri = null;
                }
                else
                {
                    throw new InvalidOperationException($"cannot set {nameof(Path)} on a '{Protocol}' proxy");
                }
            }
        }

        /// <summary>Gets or sets the parameters of this proxy. Always empty when <see cref="Endpoint"/> is not null.
        /// </summary>
        public ImmutableDictionary<string, string> Params
        {
            get => _params;
            set
            {
                CheckSupportedProtocol(nameof(Params));

                try
                {
                    CheckParams(value); // general checking (properly escape, no empty name)
                    Protocol.CheckProxyParams(value); // protocol-specific checking
                }
                catch (FormatException ex)
                {
                    throw new ArgumentException($"invalid parameters", nameof(Params), ex);
                }

                if (_endpoint != null && value.Count > 0)
                {
                    throw new InvalidOperationException($"cannot set {nameof(Params)} on a proxy with an endpoint");
                }

                _params = value;
                OriginalUri = null;
            }
        }

        /// <summary>Gets the proxy's protocol .</summary>
        public Protocol Protocol { get; }

        private ImmutableList<Endpoint> _altEndpoints = ImmutableList<Endpoint>.Empty;
        private volatile IConnection? _connection;
        private Endpoint? _endpoint;
        private string _fragment = "";
        private IInvoker _invoker = DefaultInvoker;
        private ImmutableDictionary<string, string> _params = ImmutableDictionary<string, string>.Empty;
        private string _path = "/";

        /// <summary>Creates a proxy from a connection and a path.</summary>
        /// <param name="connection">The connection of the new proxy.</param>
        /// <param name="path">The path of the proxy.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <returns>The new proxy.</returns>
        public static Proxy FromConnection(IConnection connection, string path, IInvoker? invoker = null) =>
            new(connection.Protocol)
            {
                Path = path,
                Connection = connection,
                Invoker = invoker ?? DefaultInvoker
            };

        /// <summary>Creates a relative proxy.</summary>
        /// <param name="path">The path.</param>
        /// <returns>The new relative proxy.</returns>
        public static Proxy FromPath(string path) => new(Protocol.Relative) { Path = path };

        /// <summary>Creates a proxy from a string and an invoker.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <param name="format">The proxy format to use for parsing. <c>null</c> is equivalent to
        /// <see cref="UriProxyFormat.Instance"/>.</param>
        /// <returns>The parsed proxy.</returns>
        public static Proxy Parse(string s, IInvoker? invoker = null, IProxyFormat? format = null) =>
            (format ?? UriProxyFormat.Instance).Parse(s, invoker);

        /// <summary>Tries to create a proxy from a string and invoker.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="invoker">The invoker. <c>null</c> is equivalent to <see cref="DefaultInvoker"/>.</param>
        /// <param name="format">The proxy format to use for parsing. <c>null</c> is equivalent to
        /// <see cref="UriProxyFormat.Instance"/>.</param>
        /// <param name="proxy">The parsed proxy.</param>
        /// <returns><c>true</c> when the string is parsed successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(
            string s,
            IInvoker? invoker,
            IProxyFormat? format,
            [NotNullWhen(true)] out Proxy? proxy) =>
            (format ?? UriProxyFormat.Instance).TryParse(s, invoker, out proxy);

        /// <summary>Constructs a proxy from a protocol.</summary>
        /// <param name="protocol">The protocol.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="protocol"/> is not a supported protocol or
        /// <see cref="Protocol.Relative"/>.</exception>
        public Proxy(Protocol protocol)
        {
            if (protocol.IsSupported || protocol == Protocol.Relative)
            {
                Protocol = protocol;
            }
            else
            {
                throw new ArgumentException(
                    $"protocol must be {nameof(Protocol.Relative)} or a supported protocol",
                    nameof(protocol));
            }
        }

        /// <summary>Constructs a proxy from a URI.</summary>
        public Proxy(Uri uri)
        {
            if (uri.IsAbsoluteUri)
            {
                Protocol = Protocol.FromString(uri.Scheme);
                _path = uri.AbsolutePath;
                _fragment = uri.Fragment.Length > 0 ? uri.Fragment[1..] : ""; // remove leading #

                if (Protocol.IsSupported)
                {
                    Protocol.CheckPath(_path);
                    if (!Protocol.HasFragment && _fragment.Length > 0)
                    {
                        throw new ArgumentException($"cannot create an {Protocol} proxy with a fragment", nameof(uri));
                    }

                    (ImmutableDictionary<string, string> queryParams, string? altEndpointValue) = uri.ParseQuery();

                    if (uri.Authority.Length > 0)
                    {
                        if (uri.UserInfo.Length > 0)
                        {
                            throw new ArgumentException("cannot create an endpoint with a user info", nameof(uri));
                        }

                        string host = uri.IdnHost;
                        if (host.Length == 0)
                        {
                            throw new ArgumentException("cannot create an endpoint with an empty host", nameof(uri));
                        }

                        _endpoint = new Endpoint(
                            Protocol,
                            host,
                            port: checked((ushort)(uri.Port == -1 ? Protocol.DefaultUriPort : uri.Port)),
                            queryParams);

                        if (altEndpointValue != null)
                        {
                            // Split and parse recursively each endpoint
                            foreach (string endpointStr in altEndpointValue.Split(','))
                            {
                                string altUriString = $"{uri.Scheme}://{endpointStr}";

                                // The separator for endpoint parameters in alt-endpoint is $, so we replace these '$'
                                // by '&' before sending the string to Endpoint.FromString which uses '&' as separator.
                                _altEndpoints = _altEndpoints.Add(
                                    IceRpc.Endpoint.FromString(altUriString.Replace('$', '&')));
                            }
                        }
                    }
                    else
                    {
                        if (!_path.StartsWith('/'))
                        {
                            throw new FormatException($"invalid path in proxy URI '{uri.OriginalString}'");
                        }

                        if (altEndpointValue != null)
                        {
                            throw new FormatException($"invalid alt-endpoint parameter in URI '{uri.OriginalString}'");
                        }

                        Protocol.CheckProxyParams(queryParams);
                        Params = queryParams;
                    }
                }

                // else, not a supported protocol so nothing to do
            }
            else
            {
                // relative proxy
                Protocol = Protocol.Relative;
                _path = uri.ToString();
                CheckPath(_path);
            }

            OriginalUri = uri;
        }

        /// <inheritdoc/>
        public bool Equals(Proxy? other)
        {
            if (other == null)
            {
                return false;
            }
            else if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (Protocol != other.Protocol)
            {
                return false;
            }

            if (Protocol == Protocol.Relative)
            {
                return Path == other.Path;
            }
            else if (!Protocol.IsSupported)
            {
                Debug.Assert(OriginalUri != null);
                Debug.Assert(other.OriginalUri != null);
                return OriginalUri == other.OriginalUri;
            }

            // else non-relative proxies with a supported protocol

            if (Path != other.Path)
            {
                return false;
            }
            if (Fragment != other.Fragment)
            {
                return false;
            }

            if (_endpoint != other._endpoint)
            {
                return false;
            }

            if (Invoker != other.Invoker)
            {
                return false;
            }

            // Only compare the connections of endpointless proxies.
            if (_endpoint == null && _connection != other._connection)
            {
                return false;
            }
            if (!_altEndpoints.SequenceEqual(other._altEndpoints))
            {
                return false;
            }
            if (!Params.DictionaryEqual(other.Params))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (Protocol == Protocol.Relative)
            {
                return Path.GetHashCode(StringComparison.Ordinal);
            }
            else if (!Protocol.IsSupported)
            {
                Debug.Assert(OriginalUri != null);
                return OriginalUri.GetHashCode();
            }

            // else non-relative proxy with a supported protocol

            // We only hash a subset of the properties to keep GetHashCode reasonably fast.
            var hash = new HashCode();
            hash.Add(Protocol);
            hash.Add(Path);
            hash.Add(Fragment);
            hash.Add(Invoker);

            if (_endpoint != null)
            {
                hash.Add(_endpoint);
            }
            else if (_connection != null)
            {
                hash.Add(_connection);
            }
            return hash.ToHashCode();
        }

        /// <summary>Converts this proxy into a string using the default URI format.</summary>
        public override string ToString() => ToString(UriProxyFormat.Instance);

        /// <summary>Converts this proxy into a string using a specific format.</summary>
        public string ToString(IProxyFormat format) => format.ToString(this);

        /// <summary>Converts this proxy into a Uri.</summary>
        public Uri ToUri() =>
            OriginalUri ?? (Protocol == Protocol.Relative ?
                new Uri(Path, UriKind.Relative) : new Uri(ToString(), UriKind.Absolute));

        /// <summary>Checks if <paramref name="params"/> contains properly escaped names and values.</summary>
        /// <param name="params">The dictionary to check.</param>
        /// <exception cref="FormatException">Thrown if the dictionary is not valid.</exception>
        /// <remarks>A dictionary returned by <see cref="UriExtensions.ParseQuery"/> is properly escaped.</remarks>
        internal static void CheckParams(ImmutableDictionary<string, string> @params)
        {
            foreach ((string name, string value) in @params)
            {
                if (!IsValidParamName(name))
                {
                    throw new FormatException($"invalid parameter name '{name}'");
                }
                if (!IsValidParamValue(value))
                {
                    throw new FormatException($"invalid parameter value '{value}'");
                }
            }
        }

        /// <summary>Checks if <paramref name="path"/> is a properly escaped URI absolute path, i.e. that it starts
        /// with a <c>/</c> and contains only unreserved characters, <c>%</c>, or reserved characters other than
        /// <c>?</c> and <c>#</c>.</summary>
        /// <param name="path">The path to check.</param>
        /// <exception cref="FormatException">Thrown if the path is not valid.</exception>
        /// <remarks>The absolute path of a URI with a supported protocol satisfies these requirements.</remarks>
        internal static void CheckPath(string path)
        {
            if (path.Length == 0 || path[0] != '/' || !IsValid(path, "\"<>#?\\^`{|}"))
            {
                throw new FormatException(
                    $"invalid path '{path}'; a valid path starts with '/' and contains only unreserved characters, " +
                    "'%' or reserved characters other than '?' and '#'");
            }
        }

        /// <summary>"unchecked" constructor used by the Slice decoder when decoding a Slice1 encoded proxy.</summary>
        internal Proxy(
            Protocol protocol,
            string path,
            Endpoint? endpoint,
            ImmutableList<Endpoint> altEndpoints,
            ImmutableDictionary<string, string> proxyParams,
            string fragment,
            IInvoker invoker)
        {
            Protocol = protocol;
            _path = path;
            _endpoint = endpoint;
            _altEndpoints = altEndpoints;
            _params = proxyParams;
            _fragment = fragment;
            _invoker = invoker;
        }

        /// <summary>Checks if <paramref name="fragment"/> is a properly escaped URI fragment, i.e. it contains only
        ///  unreserved characters, reserved characters or '%'.</summary>
        /// <param name="fragment">The fragment to check.</param>
        /// <exception cref="FormatException">Thrown if the fragment is not valid.</exception>
        /// <remarks>The fragment of a URI with a supported protocol satisfies these requirements.</remarks>
        private static void CheckFragment(string fragment)
        {
            if (!IsValid(fragment, "\"<>\\^`{|}"))
            {
                throw new FormatException(
                    @$"invalid fragment '{fragment
                    }'; a valid fragment contains only unreserved characters, reserved characters or '%'");
            }
        }

        private static bool IsValid(string s, string invalidChars)
        {
            // The printable ASCII character range is x20 (space) to x7E inclusive. Space is an invalid character in
            // addition to the invalid characters in the invalidChars string.
            foreach (char c in s)
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

        /// <summary>Checks if <paramref name="name"/> is not empty nor equal to <c>alt-endpoint</c> and contains only
        /// unreserved characters, <c>%</c>, or reserved characters other than <c>#</c>, <c>&#38;</c> and <c>=</c>.
        /// </summary>
        /// <param name="name">The name to check.</param>
        /// <returns><c>true</c> if <paramref name="name"/> is a valid parameter name; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>The range of valid names is much larger than the range of names you should use. For example, you
        /// should avoid parameter names with a <c>%</c> or <c>$</c> character, even though these characters are valid
        /// in a name.</remarks>
        private static bool IsValidParamName(string name) =>
            name.Length > 0 && name != "alt-endpoint" && IsValid(name, "\"<>#&=\\^`{|}");

        /// <summary>Checks if <paramref name="value"/> contains only unreserved characters, <c>%</c>, or reserved
        /// characters other than <c>#</c> and <c>&#38;</c>.</summary>
        /// <param name="value">The value to check.</param>
        /// <returns><c>true</c> if <paramref name="value"/> is a valid parameter value; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsValidParamValue(string value) => IsValid(value, "\"<>#&\\^`{|}");

        private void CheckSupportedProtocol(string propertyName)
        {
            if (Protocol == Protocol.Relative)
            {
                throw new InvalidOperationException($"cannot set {propertyName} on a relative proxy");
            }
            else if (!Protocol.IsSupported)
            {
                throw new InvalidOperationException($"cannot set {propertyName} on a '{Protocol}' proxy");
            }
        }
    }
}
