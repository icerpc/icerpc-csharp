// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace IceRpc
{
    /// <summary>An endpoint describes a server-side network sink for IceRPC requests: a server listens on an endpoint
    /// and a client establishes a connection to a given endpoint.</summary>
    public sealed record class Endpoint
    {
        /// <summary>The Ice protocol of this endpoint.</summary>
        public Protocol Protocol { get; set; }

        /// <summary>The transport of this endpoint, for example "tcp" or "quic".</summary>
        public string Transport { get; set; }

        /// <summary>The host name or address.</summary>
        public string Host { get; set; }

        /// <summary>The port number.</summary>
        public ushort Port { get; set; }

        /// <summary>Transport-specific parameters.</summary>
        public ImmutableList<EndpointParam> Params { get; set; }

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
            IceUriParser.IsEndpointUri(s) ?
                IceUriParser.ParseEndpointUri(s, Protocol.Ice2) :
                Ice1Parser.ParseEndpointString(s);

        /// <summary>Constructs a new endpoint.</summary>
        /// <param name="protocol">The Ice protocol of this endpoint.</param>
        /// <param name="transport">The transport of this endpoint, for example "tcp" or "quic".</param>
        /// <param name="host">The host name or address.</param>
        /// <param name="port">The port number.</param>
        /// <param name="params">Transport-specific parameters.</param>
        public Endpoint(
            Protocol protocol,
            string transport,
            string host,
            ushort port,
            ImmutableList<EndpointParam> @params)
        {
            Protocol = protocol;
            Transport = transport;
            Host = host;
            Port = port;
            Params = @params;
        }

        /// <summary>Checks if this endpoint is equal to another endpoint.</summary>
        /// <param name="other">The other endpoint.</param>
        /// <returns><c>true</c>when the two endpoints have the same properties, including the same parameters (in the
        /// same order); otherwise, <c>false</c>.</returns>
        public bool Equals(Endpoint? other) =>
            other != null &&
            Protocol == other.Protocol &&
            Transport == other.Transport &&
            Host == other.Host &&
            Port == other.Port &&
            Params.SequenceEqual(other.Params);

        /// <summary>Computes the hash code for this endpoint.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() =>
            HashCode.Combine(
                Protocol,
                Transport,
                Host,
                Port,
                Params.GetSequenceHashCode());

        /// <summary>Converts this endpoint into a string.</summary>
        /// <returns>The string representation of this endpoint. It's an ice+transport URI when <see cref="Protocol"/>
        /// is ice2, and an ice1 string when the Protocol is ice1.</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            if (Protocol == Protocol.Ice1)
            {
                sb.Append(Transport);

                if (Host.Length > 0)
                {
                    sb.Append(" -h ");
                    bool addQuote = Host.IndexOf(':', StringComparison.Ordinal) != -1;
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                    sb.Append(Host);
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                }

                // For backwards compatibility, we don't output "-p 0" for opaque endpoints.
                if (Transport != TransportNames.Opaque || Port != 0)
                {
                    sb.Append(" -p ");
                    sb.Append(Port.ToString(CultureInfo.InvariantCulture));
                }

                foreach ((string name, string value) in Params)
                {
                    sb.Append(' ');
                    sb.Append(name);
                    if (value.Length > 0)
                    {
                        sb.Append(' ');
                        sb.Append(value);
                    }
                }

                return sb.ToString();
            }
            else
            {
                sb.AppendEndpoint(this);
                return sb.ToString();
            }
        }
    }

    /// <summary>Equality comparer for <see cref="Endpoint"/>.</summary>
    public abstract class EndpointComparer : EqualityComparer<Endpoint>
    {
        /// <summary>An endpoint comparer that compares all endpoint properties except the parameters.</summary>
        public static EndpointComparer ParameterLess { get; } = new ParamLessEndpointComparer();

        private class ParamLessEndpointComparer : EndpointComparer
        {
            public override bool Equals(Endpoint? lhs, Endpoint? rhs) =>
                ReferenceEquals(lhs, rhs) ||
                    (lhs != null && rhs != null &&
                    lhs.Protocol == rhs.Protocol &&
                    lhs.Transport == rhs.Transport &&
                    lhs.Host == rhs.Host &&
                    lhs.Port == rhs.Port);

            public override int GetHashCode(Endpoint endpoint) =>
                HashCode.Combine(endpoint.Protocol,
                                 endpoint.Transport,
                                 endpoint.Host,
                                 endpoint.Port);
        }
    }

    // The main EndpointParam is generated by the Slice compiler.
    public readonly partial record struct EndpointParam
    {
        /// <summary>Deconstructs an endpoint parameter.</summary>
        /// <param name="name">The deconstructed name.</param>
        /// <param name="value">The deconstructed value.</param>
        public void Deconstruct(out string name, out string value)
        {
            name = Name;
            value = Value;
        }
    }
}
