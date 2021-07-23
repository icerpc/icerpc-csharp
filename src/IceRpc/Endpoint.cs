// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace IceRpc
{
    /// <summary>An endpoint describes...</summary>
    public sealed record EndpointRecord(
        Protocol Protocol,
        string TransportName,
        string Host,
        ushort Port,
        ImmutableDictionary<string, string> Options,
        ImmutableDictionary<string, string> LocalOptions
    )
    {
        /// <summary>Converts a string into an endpoint implicitly using <see cref="FromString"/>.</summary>
        /// <param name="s">The string representation of the endpoint.</param>
        /// <returns>The new endpoint.</returns>
        /// <exception cref="FormatException"><c>s</c> does not contain a valid string representation of an endpoint.
        /// </exception>
        public static implicit operator EndpointRecord(string s) => FromString(s);

        /// <summary>Creates an endpoint from its string representation.</summary>
        /// <param name="s">The string representation of the endpoint.</param>
        /// <returns>The new endpoint.</returns>
        /// <exception cref="FormatException"><c>s</c> does not contain a valid string representation of an endpoint.
        /// </exception>
        public static EndpointRecord FromString(string s) =>
            Internal.IceUriParser.IsEndpointUri(s) ? Internal.IceUriParser.ParseEndpointRecord(s) : Ice1Parser.ParseEndpointRecord(s);
    }

    /// <summary>An endpoint describes a server-side network sink for IceRPC requests: a server listens on an endpoint
    /// and a client establishes a connection to a given endpoint. Its properties are a network transport protocol such
    /// as TCP or Bluetooth RFCOMM, a host or address, a port number, and transport-specific options.</summary>
    public abstract class Endpoint : IEquatable<Endpoint>
    {
        /// <summary>Converts a string into an endpoint implicitly using <see cref="FromString"/>.</summary>
        /// <param name="s">The string representation of the endpoint.</param>
        /// <returns>The new endpoint.</returns>
        /// <exception cref="FormatException"><c>s</c> does not contain a valid string representation of an endpoint.
        /// </exception>
        public static implicit operator Endpoint(string s) => FromString(s);

        /// <summary>Creates an endpoint from its string representation.</summary>
        /// <param name="s">The string representation of the endpoint.</param>
        /// <returns>The new endpoint.</returns>
        /// <exception cref="FormatException"><c>s</c> does not contain a valid string representation of an endpoint.
        /// </exception>
        public static Endpoint FromString(string s) =>
            Internal.IceUriParser.IsEndpointUri(s) ? Internal.IceUriParser.ParseEndpoint(s) : Ice1Parser.ParseEndpoint(s);

        /// <summary>Gets the external "over the wire" representation of this endpoint. With ice2 (and up) this is the
        /// actual data structure sent and received over the wire for this endpoint. With ice1, it is a subset of this
        /// external representation.</summary>
        /// <remarks>The Options field of EndpointData is a writable list but should be treated as if it was read-only.
        /// Do not update the contents of this list.</remarks>
        public EndpointData Data { get; }

        /// <summary>The default port for the endpoint's transport.</summary>
        public virtual ushort DefaultPort => 0;

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
                        "port" => Port != 0 ? Port.ToString(CultureInfo.InvariantCulture) : null,
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

        /// <summary>The <see cref="IceRpc.TransportCode"></see> of this endpoint.</summary>
        public TransportCode TransportCode => Data.TransportCode;

        /// <summary>The name of the endpoint's transport in lowercase.</summary>
        public virtual string TransportName => TransportCode.ToString().ToLowerInvariant();

        /// <summary>Indicates whether or not this endpoint has options with non default values that ToString prints.
        /// Always true for ice1 endpoints.</summary>
        protected internal virtual bool HasOptions => Protocol == Protocol.Ice1;

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
            other is Endpoint endpoint && Protocol == endpoint.Protocol && Data == endpoint.Data;

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
                bool addQuote = Host.IndexOf(':', StringComparison.InvariantCulture) != -1;
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

        /// <summary>Provides the same hash code for two equivalent endpoints. See <see cref="IsEquivalent"/>.</summary>
        protected internal virtual int GetEquivalentHashCode() => GetHashCode();

        /// <summary>Two endpoints are considered equivalent if they are equal or their differences should not trigger
        /// the establishment of separate connections to those endpoints. For example, two tcp endpoints that are
        /// identical except for their ice1 Timeout and HasCompressionFlag properties are equivalent but are not equal.
        /// </summary>
        protected internal virtual bool IsEquivalent(Endpoint other) => Equals(other);

        /// <summary>Encodes the options of this endpoint. Used only when encoding ice1 proxies with the 1.1 encoding.
        /// </summary>
        /// <param name="encoder">The Ice encoder.</param>
        protected internal abstract void EncodeOptions11(IceEncoder encoder);

        /// <summary>Constructs a new endpoint</summary>
        /// <param name="data">The <see cref="EndpointData"/> struct.</param>
        /// <param name="protocol">The endpoint's protocol.</param>
        protected Endpoint(EndpointData data, Protocol protocol)
        {
            Data = data;
            Protocol = protocol;
        }
    }

    /// <summary>This class contains <see cref="Endpoint"/> extension methods.</summary>
    public static class EndpointExtensions
    {
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

            if (endpoint.Host.Contains(':', StringComparison.InvariantCulture))
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
