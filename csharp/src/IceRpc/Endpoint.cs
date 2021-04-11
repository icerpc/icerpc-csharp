// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An endpoint describes a server-side network sink for Ice requests: a server listens on one or
    /// more endpoints and a client establishes a connection to a given server endpoint. Its properties are
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
            UriParser.IsEndpointUri(s) ? UriParser.ParseEndpoint(s) : Ice1Parser.ParseEndpoint(s);

        /// <summary>Gets the external "over the wire" representation of this endpoint. With ice2 (and up) this is the
        /// actual data structure sent and received over the wire for this endpoint. With ice1, it is a subset of this
        /// external representation.</summary>
        /// <remarks>The Options field of EndpointData is a writable array but should be treated as if it was read-only.
        /// Do not update the contents of this array.</remarks>
        public EndpointData Data { get; }

        /// <summary>The host name or address.</summary>
        public string Host => Data.Host;

        /// <summary>Indicates whether or not this endpoint's transport is always secure. Only applies to ice1.</summary>
        /// <value>True when this endpoint's transport is secure; otherwise, false.</value>
        public virtual bool IsAlwaysSecure => false;

        /// <summary>Indicates whether or not this endpoint's transport uses datagrams with no ordering or delivery
        /// guarantees.</summary>
        /// <value>True when this endpoint's transport is datagram-based; otherwise, false. There is currently a
        /// single datagram-based transport: UDP.</value>
        public virtual bool IsDatagram => false;

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

        /// <summary>Creates a connection to this endpoint.</summary>
        /// <param name="options">The client connection options.</param>
        /// <param name="logger">The transport logger.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The new established connection.</returns>
        protected internal abstract Task<Connection> ConnectAsync(
            OutgoingConnectionOptions options,
            ILogger logger,
            CancellationToken cancel);

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
        protected internal abstract void AppendOptions(StringBuilder sb, char optionSeparator);

        /// <summary>Provides the same hash code for two equivalent endpoints. See <see cref="IsEquivalent"/>.</summary>
        protected internal virtual int GetEquivalentHashCode() => GetHashCode();

        /// <summary>Two endpoints are considered equivalent if they are equal or their differences should not trigger
        /// the establishment of separate connections to those endpoints. For example, two tcp endpoints that are
        /// identical except for their ice1 HashCompressedFlag property are equivalent but are not equal.</summary>
        protected internal virtual bool IsEquivalent(Endpoint other) => Equals(other);

        /// <summary>Writes the options of this endpoint to the output stream. Used only when marshaling ice1 proxies
        /// with the 1.1 encoding.</summary>
        /// <param name="ostr">The output stream.</param>
        protected internal abstract void WriteOptions11(OutputStream ostr);

        /// <summary>Returns an acceptor for this endpoint. An acceptor listens for connection establishment requests
        /// from clients and creates a new connection for each client. This is typically used to implement a
        /// stream-based transport. Datagram transports don't implement this method but instead implement the
        /// <see cref="CreateDatagramServerConnection"/> method.</summary>
        /// <param name="server">The server associated to the acceptor.</param>
        /// <returns>An acceptor for this endpoint.</returns>
        public abstract IAcceptor Acceptor(Server server);

        /// <summary>Creates a datagram server side connection for this endpoint to receive datagrams from clients.
        /// Unlike stream-based transports, datagram endpoints don't support an acceptor responsible for accepting new
        /// connections but implement this method to provide a connection responsible for receiving datagrams from
        /// clients.</summary>
        /// <returns>The datagram server side connection.</returns>
        public abstract Connection CreateDatagramServerConnection(Server server);

        /// <summary>Returns the published endpoint for this server endpoint.</summary>
        /// <param name="publishedHost">The host portion of the published endpoint when the endpoint's type supports
        /// DNS resolution of its hosts. Otherwise, <c>publishedHost</c> is not used.</param>
        /// <returns>The published endpoint.</returns>
        protected internal abstract Endpoint GetPublishedEndpoint(string publishedHost);

        /// <summary>Constructs a new endpoint</summary>
        /// <param name="data">The <see cref="EndpointData"/> struct.</param>
        /// <param name="protocol">The endpoint's protocol.</param>
        protected Endpoint(EndpointData data, Protocol protocol)
        {
            Data = data;
            Protocol = protocol;
        }
    }

    public static class EndpointExtensions
    {
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

        internal static StringBuilder AppendEndpointList(
            this StringBuilder sb,
            IReadOnlyList<Endpoint> endpoints)
        {
            Debug.Assert(endpoints.Count > 0);

            if (endpoints[0].Protocol == Protocol.Ice1)
            {
                sb.Append(string.Join(":", endpoints));
            }
            else
            {
                sb.AppendEndpoint(endpoints[0]);
                if (endpoints.Count > 1)
                {
                    Transport mainTransport = endpoints[0].Transport;
                    sb.Append("?alt-endpoint=");
                    for (int i = 1; i < endpoints.Count; ++i)
                    {
                        if (i > 1)
                        {
                            sb.Append(',');
                        }
                        sb.AppendEndpoint(endpoints[i], "", mainTransport != endpoints[i].Transport, '$');
                    }
                }
            }
            return sb;
        }
    }
}
