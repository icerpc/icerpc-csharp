// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace IceRpc
{
    /// <summary>A proxy is a local ambassador for a remote IceRPC service, and can send requests to this remote
    /// service. It can also be encoded in requests and responses. There are two forms of proxies: untyped proxies
    /// (instances of this class), and typed proxies that are Prx structs generated by the Slice compiler. Each typed
    /// proxy struct holds only a Proxy.</summary>
    /// <seealso cref="Slice.IPrx"/>
    public sealed class Proxy : IEquatable<Proxy>
    {
        /// <summary>Gets or sets the secondary endpoints of this proxy.</summary>
        /// <value>The secondary endpoints of this proxy.</value>
        public ImmutableList<Endpoint> AltEndpoints
        {
            get => _altEndpoints;

            set
            {
                if (value.Count > 0)
                {
                    if (_endpoint == null)
                    {
                        throw new ArgumentException(
                            $"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)} is empty",
                            nameof(AltEndpoints));
                    }

                    if (_endpoint.Transport == TransportNames.Coloc)
                    {
                        throw new ArgumentException(
                            @$"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)} uses the coloc transport",
                            nameof(AltEndpoints));
                    }

                    if (value.Any(e => e.Transport == TransportNames.Coloc))
                    {
                        throw new ArgumentException("cannot use coloc transport", nameof(AltEndpoints));
                    }

                    if (value.Any(e => e.Protocol != Protocol))
                    {
                        throw new ArgumentException($"the protocol of all endpoints must be {Protocol.Name}",
                                                    nameof(AltEndpoints));
                    }

                    if (Protocol == Protocol.Ice1)
                    {
                        if (_endpoint.Transport == TransportNames.Loc)
                        {
                            throw new ArgumentException(
                                @$"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)} uses the loc transport",
                                nameof(AltEndpoints));
                        }

                        if (value.Any(e => e.Transport == TransportNames.Loc))
                        {
                            throw new ArgumentException("cannot use loc transport", nameof(AltEndpoints));
                        }
                    }
                }
                // else, no need to check anything, an empty list is always fine.

                _altEndpoints = value;
            }
        }

        /// <summary>Gets or sets the connection of this proxy. Setting the connection does not affect the proxy
        /// endpoints (if any); in particular, set does check the new connection is compatible with these endpoints.
        /// </summary>
        /// <value>The connection for this proxy, or null if the proxy does not have a connection.</value>
        public Connection? Connection
        {
            get => _connection;
            set => _connection = value;
        }

        /// <summary>The encoding that a caller should use when encoding request parameters when such a caller supports
        /// multiple encodings. Its value is usually the encoding of the protocol.</summary>
        public Encoding Encoding { get; set; }

        /// <summary>Gets or sets the main endpoint of this proxy.</summary>
        /// <value>The main endpoint of this proxy, or null if this proxy has no endpoint.</value>
        public Endpoint? Endpoint
        {
            get => _endpoint;

            set
            {
                if (value != null)
                {
                    if (value.Protocol != Protocol)
                    {
                        throw new ArgumentException("the new endpoint must use the proxy's protocol",
                                                    nameof(Endpoint));
                    }

                    if (_altEndpoints.Count > 0 && value.Transport == TransportNames.Coloc)
                    {
                        throw new ArgumentException(
                            "a proxy with a coloc endpoint cannot have alt endpoints", nameof(Endpoint));
                    }

                    if (Protocol == Protocol.Ice1 && _altEndpoints.Count > 0 && value.Transport == TransportNames.Loc)
                    {
                        throw new ArgumentException(
                            "an ice1 proxy with a loc endpoint cannot have alt endpoints", nameof(Endpoint));
                    }
                }
                else if (_altEndpoints.Count > 0)
                {
                    throw new ArgumentException(
                        $"cannot clear {nameof(Endpoint)} when {nameof(AltEndpoints)} is not empty",
                        nameof(Endpoint));
                }
                _endpoint = value;
            }
        }

        /// <summary>Gets or sets the invoker of this proxy.</summary>
        public IInvoker? Invoker { get; set; }

        /// <summary>Gets the path of this proxy. This path is a percent-escaped URI path.</summary>
        // private set only used in WithPath
        public string Path { get; private set; }

        /// <summary>The Ice protocol of this proxy. Requests sent with this proxy use only this Ice protocol.</summary>
        public Protocol Protocol { get; }

        private ImmutableList<Endpoint> _altEndpoints = ImmutableList<Endpoint>.Empty;
        private volatile Connection? _connection;
        private Endpoint? _endpoint;

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Proxy? lhs, Proxy? rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (lhs is null || rhs is null)
            {
                return false;
            }
            return lhs.Equals(rhs);
        }

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(Proxy? lhs, Proxy? rhs) => !(lhs == rhs);

        /// <summary>Creates a proxy from a connection and a path.</summary>
        /// <param name="connection">The connection of the new proxy. If it's a client connection, the endpoint of the
        /// new proxy is <see cref="Connection.RemoteEndpoint"/>; otherwise, the new proxy has no endpoint.</param>
        /// <param name="path">The path of the proxy.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <returns>The new proxy.</returns>
        public static Proxy FromConnection(Connection connection, string path, IInvoker? invoker = null)
        {
            var proxy = new Proxy(path, connection.Protocol);
            proxy.Endpoint = connection.IsServer ? null : connection.RemoteEndpoint;
            proxy.Connection = connection;
            proxy.Invoker = invoker;
            return proxy;
        }

        /// <summary>Creates a proxy from a path and protocol.</summary>
        /// <param name="path">The path.</param>
        /// <param name="protocol">The protocol.</param>
        /// <returns>The new proxy.</returns>
        public static Proxy FromPath(string path, Protocol? protocol = null) => new(path, protocol ?? Protocol.Ice2);

        /// <summary>Creates a proxy from a string and an invoker.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="invoker">The invoker of the new proxy.</param>
        /// <returns>The parsed proxy.</returns>
        public static Proxy Parse(string s, IInvoker? invoker = null)
        {
            string proxyString = s.Trim();
            if (proxyString.Length == 0)
            {
                throw new FormatException("an empty string does not represent a proxy");
            }

            Proxy proxy = IceUriParser.IsProxyUri(proxyString) ?
                IceUriParser.ParseProxyUri(proxyString) : Ice1Parser.ParseProxyString(proxyString);

            proxy.Invoker = invoker;
            return proxy;
        }

        /// <summary>Tries to create a proxy from a string and invoker.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="proxy">The parsed proxy.</param>
        /// <returns><c>true</c> when the string is parsed successfully; otherwise, <c>false</c>.</returns>
        public static bool TryParse(string s, IInvoker? invoker, [NotNullWhen(true)] out Proxy? proxy)
        {
            try
            {
                proxy = Parse(s, invoker);
                return true;
            }
            catch
            {
                proxy = null;
                return false;
            }
        }

        /// <summary>Creates a shallow copy of this proxy. It's a safe copy since the only container property
        /// (AltEndpoints) is immutable.</summary>
        /// <returns>A copy of this proxy.</returns>
        public Proxy Clone() => (Proxy)MemberwiseClone();

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

            if (Encoding != other.Encoding)
            {
                return false;
            }
            if (_endpoint != other._endpoint)
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
            if (Invoker != other.Invoker)
            {
                return false;
            }
            if (Path != other.Path)
            {
                return false;
            }
            if (Protocol != other.Protocol)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as Proxy);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            // We only hash a subset of the properties to keep GetHashCode reasonably fast.
            var hash = new HashCode();
            hash.Add(Invoker);
            hash.Add(Path);
            hash.Add(Protocol);
            if (_endpoint != null)
            {
                hash.Add(_endpoint.GetHashCode());
            }
            else if (_connection != null)
            {
                hash.Add(_connection);
            }
            return hash.ToHashCode();
        }

        /// <inherit-doc/>
        public override string ToString()
        {
            if (Protocol == Protocol.Ice1)
            {
                return ToString(default);
            }
            else // >= ice2, use URI format
            {
                var sb = new StringBuilder();
                bool firstOption = true;

                if (_endpoint != null)
                {
                    // Use ice+transport scheme
                    sb.AppendEndpoint(_endpoint, Path);

                    firstOption = _endpoint.Protocol == Protocol.Ice2 && _endpoint.Params.Count == 0;
                }
                else
                {
                    sb.Append("ice:"); // endpointless proxy
                    sb.Append(Path);

                    // TODO: append protocol
                }

                if (Encoding != Ice2Definitions.Encoding)
                {
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("encoding=");
                    sb.Append(Encoding);
                }

                if (_altEndpoints.Count > 0)
                {
                    string mainTransport = _endpoint!.Transport;
                    StartQueryOption(sb, ref firstOption);
                    sb.Append("alt-endpoint=");
                    for (int i = 0; i < _altEndpoints.Count; ++i)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }
                        sb.AppendEndpoint(_altEndpoints[i], "", mainTransport != _altEndpoints[i].Transport, '$');
                    }
                }

                return sb.ToString();
            }

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

        /// <summary>Converts a proxy into a string, using the format specified by ToStringMode.</summary>
        /// <param name="mode">Specifies how non-printable ASCII characters are escaped in the resulting string. See
        /// <see cref="ToStringMode"/>.</param>
        /// <returns>The string representation of this proxy.</returns>
        public string ToString(ToStringMode mode)
        {
            if (Protocol != Protocol.Ice1)
            {
                return ToString();
            }

            var identityAndFacet = IdentityAndFacet.FromPath(Path);

            var sb = new StringBuilder();

            // If the encoded identity string contains characters which the reference parser uses as separators,
            // then we enclose the identity string in quotes.
            string id = identityAndFacet.Identity.ToString(mode);
            if (StringUtil.FindFirstOf(id, " :@") != -1)
            {
                sb.Append('"');
                sb.Append(id);
                sb.Append('"');
            }
            else
            {
                sb.Append(id);
            }

            if (identityAndFacet.Facet.Length > 0)
            {
                // If the encoded facet string contains characters which the reference parser uses as separators,
                // then we enclose the facet string in quotes.
                sb.Append(" -f ");
                string fs = StringUtil.EscapeString(identityAndFacet.Facet, mode);
                if (StringUtil.FindFirstOf(fs, " :@") != -1)
                {
                    sb.Append('"');
                    sb.Append(fs);
                    sb.Append('"');
                }
                else
                {
                    sb.Append(fs);
                }
            }

            if (Endpoint?.Transport == TransportNames.Udp)
            {
                sb.Append(" -d");
            }
            else
            {
                sb.Append(" -t");
            }

            // Always print the encoding version to ensure a stringified proxy will convert back to a proxy with the
            // same encoding with StringToProxy. (Only needed for backwards compatibility).
            sb.Append(" -e ");
            sb.Append(Encoding);

            if (Endpoint != null)
            {
                if (Endpoint.Transport == TransportNames.Loc)
                {
                    string adapterId = Endpoint.Host;

                    sb.Append(" @ ");

                    // If the encoded adapter ID contains characters which the proxy parser uses as separators, then
                    // we enclose the adapter ID string in double quotes.
                    adapterId = StringUtil.EscapeString(adapterId, mode);
                    if (StringUtil.FindFirstOf(adapterId, " :@") != -1)
                    {
                        sb.Append('"');
                        sb.Append(adapterId);
                        sb.Append('"');
                    }
                    else
                    {
                        sb.Append(adapterId);
                    }
                }
                else
                {
                    sb.Append(':');
                    sb.Append(Endpoint);

                    foreach (Endpoint e in AltEndpoints)
                    {
                        sb.Append(':');
                        sb.Append(e);
                    }
                }
            }
            return sb.ToString();
        }

        /// <summary>Creates a copy of this proxy with a new path.</summary>
        /// <param name="path">The new path.</param>
        /// <returns>A new proxy with the specified path.</returns>
        public Proxy WithPath(string path)
        {
            Proxy proxy = Clone();
            proxy.Path = path;

            if (Protocol == Protocol.Ice1 && proxy.Endpoint == null)
            {
                // clear cached connection of well-known proxy
                proxy.Connection = null;
            }
            return proxy;
        }

        /// <summary>Constructs a new proxy.</summary>
        /// <param name="path">The proxy path.</param>
        /// <param name="protocol">The proxy protocol.</param>
        internal Proxy(string path, Protocol protocol)
        {
            Protocol = protocol;
            IceUriParser.CheckPath(path, nameof(path));
            Path = path;
            Encoding = Protocol.GetIceEncoding() ?? Encoding.Unknown;
        }
    }
}
