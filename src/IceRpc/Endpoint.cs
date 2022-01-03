// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.ComponentModel;

namespace IceRpc
{
    /// <summary>An endpoint describes a server-side network sink for IceRPC requests: a server listens on an endpoint
    /// and a client establishes a connection to a given endpoint.</summary>
    [TypeConverter(typeof(EndpointTypeConverter))]
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
        public static Endpoint FromString(string s) => UriProxyFormat.ParseEndpoint(s, Protocol.Ice2);

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
        /// <returns>The string representation of this endpoint. It's an icerpc+transport URI when <see cref="Protocol"/>
        /// is icerpc, and an ice string when the Protocol is ice.</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendEndpoint(this);
            return sb.ToString();
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

    /// <summary>The endpoint type converter specifies how to convert a string to an endpoint. It's used by sub-systems
    /// such as the Microsoft ConfigurationBinder to bind string values to Endpoint properties.</summary>
    public class EndpointTypeConverter : TypeConverter
    {
        /// <inheritdoc/>
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        /// <inheritdoc/>
        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string valueStr)
            {
                return Endpoint.FromString(valueStr);
            }
            else
            {
                return base.ConvertFrom(context, culture, value);
            }
        }
    }
}
