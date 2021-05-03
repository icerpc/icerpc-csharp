// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace IceRpc
{
    /// <summary>An endpoint describes a server-side network sink for Ice requests: a server listens on one or
    /// more endpoints and a client establishes a socket to a given server endpoint. Its properties are
    /// a network transport protocol such as TCP or Bluetooth RFCOMM, a host or address, a port number, and
    /// transport-specific options.</summary>
    public abstract class Endpoint : IEquatable<Endpoint>
    {
        /// <summary>Creates an endpoint from its string representation.</summary>
        /// <param name="s">The string representation of the endpoint.</param>
        /// <returns>The parsed endpoint.</returns>
        /// <exception cref="FormatException"><c>s</c> does not contain a valid string representation of an endpoint.
        /// </exception>
        public static Endpoint Parse(string s) =>
            Internal.UriParser.IsEndpointUri(s) ? Internal.UriParser.ParseEndpoint(s) : Ice1Parser.ParseEndpoint(s);

        /// <summary>Creates an endpoint from its string representation.</summary>
        /// <param name="s">The string representation of the endpoint.</param>
        /// <param name="endpoint">The parsed endpoint.</param>
        /// <returns>True when <c>s</c> is a valid endpoint string and endpoint is not null; otherwise, false.
        /// </returns>
        public static bool TryParse(string s, out Endpoint? endpoint)
        {
            try
            {
                endpoint = Parse(s);
                return true;
            }
            catch (FormatException)
            {
                endpoint = null;
                return false;
            }
        }

        /// <summary>Gets the external "over the wire" representation of this endpoint. With ice2 (and up) this is the
        /// actual data structure sent and received over the wire for this endpoint. With ice1, it is a subset of this
        /// external representation.</summary>
        /// <remarks>The Options field of EndpointData is a writable array but should be treated as if it was read-only.
        /// Do not update the contents of this array.</remarks>
        public EndpointData Data { get; }

        /// <summary>The host name or address.</summary>
        public string Host => Data.Host;

        /// <summary>Indicates whether or not this endpoint's transport uses datagrams with no ordering or delivery
        /// guarantees.</summary>
        /// <value>True when this endpoint's transport is datagram-based; otherwise, false. There is currently a
        /// single datagram-based transport: UDP.</value>
        public virtual bool IsDatagram => false;

        /// <summary>Indicates whether or not this endpoint's transport is secure.</summary>
        /// <value>True means the endpoint's transport is secure. False means the endpoint's tranport is not secure. And
        /// null means whether or not the transport is secure is not determined yet. The endpoint of an established
        /// connection never returns this null value.</value>
        public virtual bool? IsSecure => null;

        /// <summary>Gets an option of the endpoint.</summary>
        /// <param name="option">The name of the option to retrieve.</param>
        /// <value>The value of this option, or null if this option is unknown, not set or set to its default value.
        /// </value>
        public virtual string? this[string option]
        {
            get
            {
                if (Protocol == Protocol.Ice1)
                {
                    return option switch
                    {
                        "host" => Host.Length > 0 ? Host : null,
                        "port" => Port != DefaultPort ? Port.ToString(CultureInfo.InvariantCulture) : null,
                        _ => null,
                    };
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>The port number.</summary>
        public ushort Port => Data.Port;

        /// <summary>The Ice protocol of this endpoint.</summary>
        public Protocol Protocol { get; }

        /// <summary>The scheme for this endpoint. With ice1, it's the transport name (tcp, ssl etc.) or opaque. With
        /// ice2, it's ice+transport (ice+tcp, ice+quic etc.) or ice+universal.</summary>
        public virtual string Scheme => Protocol == Protocol.Ice1 ? TransportName : $"ice+{TransportName}";

        /// <summary>The <see cref="IceRpc.Transport"></see> of this endpoint.</summary>
        public Transport Transport => Data.Transport;

        /// <summary>The name of the endpoint's transport in lowercase.</summary>
        public virtual string TransportName => Transport.ToString().ToLowerInvariant();

        /// <summary>Gets the default port of this endpoint.</summary>
        protected internal abstract ushort DefaultPort { get; }

        /// <summary>Returns true when Host is a DNS name.</summary>
        protected internal virtual bool HasDnsHost => false;

        /// <summary>Indicates whether or not this endpoint has options with non default values that ToString would
        /// print. Always true for ice1 endpoints.</summary>
        protected internal abstract bool HasOptions { get; }

        /// <summary>The equality operator == returns true if its operands are equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are equal, otherwise <c>false</c>.</returns>
        public static bool operator ==(Endpoint? lhs, Endpoint? rhs)
        {
            if (ReferenceEquals(lhs, rhs))
            {
                return true;
            }

            if (lhs is null || rhs is null)
            {
                return false;
            }
            return rhs.Equals(lhs);
        }

        /// <summary>The inequality operator != returns true if its operands are not equal, false otherwise.</summary>
        /// <param name="lhs">The left hand side operand.</param>
        /// <param name="rhs">The right hand side operand.</param>
        /// <returns><c>true</c> if the operands are not equal, otherwise <c>false</c>.</returns>
        public static bool operator !=(Endpoint? lhs, Endpoint? rhs) => !(lhs == rhs);

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Endpoint other && Equals(other);

        /// <inheritdoc/>
        public virtual bool Equals(Endpoint? other) =>
            other is Endpoint endpoint &&
                Protocol == endpoint.Protocol &&
                Data == endpoint.Data;

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Protocol, Data);

        /// <summary>Converts the endpoint into a string. The format of this string depends on the protocol: either
        /// ice1 format (for ice1) or URI format (for ice2 and up).</summary>
        public override string ToString()
        {
            if (Protocol == Protocol.Ice1)
            {
                var sb = new StringBuilder(Scheme);
                AppendOptions(sb, ' '); // option separator is not used with ice1
                return sb.ToString();
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendEndpoint(this);
                return sb.ToString();
            }
        }

        /// <summary>Appends the options of this endpoint with non default values to the string builder.</summary>
        /// <param name="sb">The string builder.</param>
        /// <param name="optionSeparator">The character used to separate two options. This separator is not used for
        /// ice1 endpoints.</param>
        protected internal virtual void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            if (Protocol == Protocol.Ice1)
            {
                Debug.Assert(Host.Length > 0);
                sb.Append(" -h ");
                bool addQuote = Host.IndexOf(':') != -1;
                if (addQuote)
                {
                    sb.Append('"');
                }
                sb.Append(Host);
                if (addQuote)
                {
                    sb.Append('"');
                }

                sb.Append(" -p ");
                sb.Append(Port.ToString(CultureInfo.InvariantCulture));
            }
            // else by default, no option to append.
        }

        /// <summary>Returns an acceptor for this endpoint. An acceptor listens for connection establishment requests
        /// from clients and creates a new socket for each client. This is typically used to implement a
        /// stream-based transport such as TCP or Quic. Datagram or serial transports don't implement this method but
        /// instead implement the <see cref="CreateServerSocket"/> method.</summary>
        /// <param name="server">The server associated to the acceptor.</param>
        /// <returns>An acceptor for this endpoint.</returns>
        protected internal virtual IAcceptor CreateAcceptor(Server server) =>
            throw new NotSupportedException($"endpoint '{this}' cannot accept connections");

        /// <summary>Creates a client socket for this endpoint.</summary>
        /// <param name="options">The client connection options.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The client socket.</returns>
        protected internal virtual MultiStreamSocket CreateClientSocket(
            OutgoingConnectionOptions options,
            ILogger logger) =>
            throw new NotSupportedException($"cannot establish a connection to endpoint '{this}'");

        /// <summary>Creates a server socket for this endpoint to receive data from one or multiple client.
        /// This is used to implement a transport which can only communicate with a single client (e.g. a serial
        /// based transport) or which can received data from multiple clients with a single socket (e.g: UDP).</summary>
        /// <param name="options">The server connection options.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The server socket.</returns>
        protected internal virtual MultiStreamSocket CreateServerSocket(
            IncomingConnectionOptions options,
            ILogger logger) =>
            throw new NotSupportedException($"endpoint '{this}' cannot create a server connection");

        /// <summary>Provides the same hash code for two equivalent endpoints. See <see cref="IsEquivalent"/>.</summary>
        protected internal virtual int GetEquivalentHashCode() => GetHashCode();

        /// <summary>Returns the proxy endpoint for this server endpoint.</summary>
        /// <param name="proxyHost">The host portion of the proxy endpoint when the endpoint's type supports DNS
        /// resolution of its hosts. Otherwise, <c>proxyHost</c> is not used.</param>
        /// <returns>The proxy endpoint.</returns>
        protected internal virtual Endpoint GetProxyEndpoint(string proxyHost) => this;

        /// <summary>Two endpoints are considered equivalent if they are equal or their differences should not trigger
        /// the establishment of separate connections to those endpoints. For example, two tcp endpoints that are
        /// identical except for their ice1 HashCompressedFlag property are equivalent but are not equal.</summary>
        protected internal virtual bool IsEquivalent(Endpoint other) => Equals(other);

        /// <summary>Writes the options of this endpoint to the output stream. Used only when marshaling ice1 proxies
        /// with the 1.1 encoding.</summary>
        /// <param name="ostr">The output stream.</param>
        protected internal abstract void WriteOptions11(OutputStream ostr);

        /// <summary>Constructs a new endpoint</summary>
        /// <param name="data">The <see cref="EndpointData"/> struct.</param>
        /// <param name="protocol">The endpoint's protocol.</param>
        protected Endpoint(EndpointData data, Protocol protocol)
        {
            Data = data;
            Protocol = protocol;
        }

        /// <summary>Parses host and port from an ice1 endpoint string.</summary>
        private protected static (string Host, ushort Port) ParseHostAndPort(
            Dictionary<string, string?> options,
            string endpointString)
        {
            string host;
            ushort port = 0;

            if (options.TryGetValue("-h", out string? argument))
            {
                host = argument ??
                    throw new FormatException($"no argument provided for -h option in endpoint '{endpointString}'");

                if (host == "*")
                {
                    // TODO: Should we check that IPv6 is enabled first and use 0.0.0.0 otherwise, or will
                    // ::0 just bind to the IPv4 addresses in this case?
                    host = "::0";
                }

                options.Remove("-h");
            }
            else
            {
                throw new FormatException($"no -h option in endpoint '{endpointString}'");
            }

            if (options.TryGetValue("-p", out argument))
            {
                if (argument == null)
                {
                    throw new FormatException($"no argument provided for -p option in endpoint '{endpointString}'");
                }

                try
                {
                    port = ushort.Parse(argument, CultureInfo.InvariantCulture);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"invalid port value '{argument}' in endpoint '{endpointString}'", ex);
                }
                options.Remove("-p");
            }
            // else port remains 0

            return (host, port);
        }
    }

    public static class EndpointExtensions
    {
        /// <summary>Dictionary of non-coloc endpoint to coloc endpoint used by ToColocEndpoint.</summary>
        private static readonly IDictionary<Endpoint, ColocEndpoint> _colocRegistry =
            new ConcurrentDictionary<Endpoint, ColocEndpoint>(EndpointComparer.Equivalent);

        /// <summary>Returns the corresponding endpoint for the coloc transport, if there is one.</summary>
        /// <param name="endpoint">The endpoint to check.</param>
        /// <returns>The corresponding endpoint for the coloc transport, or null if there is no such endpoint</returns>
        public static Endpoint? ToColocEndpoint(this Endpoint endpoint) =>
            endpoint.Transport == Transport.Coloc ? endpoint :
                (_colocRegistry.TryGetValue(endpoint, out ColocEndpoint? colocEndpoint) ? colocEndpoint : null);

        /// <summary>Creates an endpoint from an <see cref="EndpointData"/> struct.</summary>
        /// <param name="data">The endpoint's data.</param>
        /// <param name="protocol">The endpoint's protocol.</param>
        /// <returns>A new endpoint.</returns>
        /// <remarks>If the transport is not registered, this method returns a <see cref="UniversalEndpoint"/> when
        /// <c>protocol</c> is ice2 or greater, and throws <see cref="NotSupportedException"/> when <c>protocol</c> is
        /// ice1.</remarks>
        public static Endpoint ToEndpoint(this EndpointData data, Protocol protocol)
        {
            if (Runtime.FindEndpointFactory(data.Transport) is EndpointFactory factory)
            {
                return factory(data, protocol);
            }

            return protocol != Protocol.Ice1 ? UniversalEndpoint.Create(data, protocol) :
                throw new NotSupportedException(
                    $"cannot create endpoint for ice1 protocol and transport '{data.Transport}'");
        }

        /// <summary>Appends the endpoint and all its options (if any) to this string builder, when using the URI
        /// format.</summary>
        /// <param name="sb">The string builder.</param>
        /// <param name="endpoint">The endpoint to append.</param>
        /// <param name="path">The path of the endpoint URI. Use this parameter to start building a proxy URI.</param>
        /// <param name="includeScheme">When true, first appends the endpoint's scheme followed by ://.</param>
        /// <param name="optionSeparator">The character that separates options in the query component of the URI.
        /// </param>
        /// <returns>The string builder parameter.</returns>
        internal static StringBuilder AppendEndpoint(
            this StringBuilder sb,
            Endpoint endpoint,
            string path = "",
            bool includeScheme = true,
            char optionSeparator = '&')
        {
            Debug.Assert(endpoint.Protocol != Protocol.Ice1); // we never generate URIs for the ice1 protocol

            if (includeScheme)
            {
                sb.Append(endpoint.Scheme);
                sb.Append("://");
            }

            if (endpoint.Host.Contains(':'))
            {
                sb.Append('[');
                sb.Append(endpoint.Host);
                sb.Append(']');
            }
            else
            {
                sb.Append(endpoint.Host);
            }

            if (endpoint.Port != endpoint.DefaultPort)
            {
                sb.Append(':');
                sb.Append(endpoint.Port.ToString(CultureInfo.InvariantCulture));
            }

            if (path.Length > 0)
            {
                sb.Append(path);
            }

            if (endpoint.HasOptions)
            {
                sb.Append('?');
                endpoint.AppendOptions(sb, optionSeparator);
            }
            return sb;
        }

        internal static void RegisterColocEndpoint(Endpoint endpoint, ColocEndpoint colocEndpoint)
        {
            Debug.Assert(endpoint.Transport != Transport.Coloc);
            if (!_colocRegistry.TryAdd(endpoint, colocEndpoint))
            {
                Debug.Assert(false);
                throw new TransportException($"endpoint '{endpoint}' is already registered for coloc");
            }
        }

        internal static void UnregisterColocEndpoint(Endpoint endpoint) =>
            _colocRegistry.Remove(endpoint);
    }

    internal abstract class EndpointComparer : EqualityComparer<Endpoint>
    {
        internal static EndpointComparer Equivalent { get; } = new EquivalentEndpointComparer();

        private class EquivalentEndpointComparer : EndpointComparer
        {
            public override bool Equals(Endpoint? lhs, Endpoint? rhs) => lhs!.IsEquivalent(rhs!);
            public override int GetHashCode(Endpoint endpoint) => endpoint.GetEquivalentHashCode();
        }
    }
}
