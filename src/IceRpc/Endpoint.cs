// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace IceRpc
{
    /// <summary>An endpoint describes a server-side network sink for IceRPC requests: a server listens on an endpoint
    /// and a client establishes a connection to a given endpoint.</summary>
    [TypeConverter(typeof(EndpointTypeConverter))]
    public sealed record class Endpoint
    {
        /// <summary>The protocol of this endpoint.</summary>
        public Protocol Protocol { get; set; }

        /// <summary>The host name or address.</summary>
        public string Host { get; set; }

        /// <summary>The port number.</summary>
        public ushort Port { get; set; }

        /// <summary>Transport-specific parameters.</summary>
        public ImmutableDictionary<string, string> Params { get; set; }

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
        public static Endpoint FromString(string s) => UriProxyFormat.ParseEndpoint(s);

        /// <summary>Constructs a new endpoint.</summary>
        /// <param name="protocol">The protocol of this endpoint.</param>
        /// <param name="host">The host name or address.</param>
        /// <param name="port">The port number.</param>
        /// <param name="params">Transport-specific parameters.</param>
        public Endpoint(
            Protocol protocol,
            string host,
            ushort port,
            ImmutableDictionary<string, string> @params)
        {
            Protocol = protocol;
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
            Host == other.Host &&
            Port == other.Port &&
            Params.DictionaryEqual(other.Params);

        /// <summary>Computes the hash code for this endpoint.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => HashCode.Combine(Protocol, Host, Port, Params.Count);

        /// <summary>Converts this endpoint into a string.</summary>
        /// <returns>The string representation of this endpoint.</returns>
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
                    lhs.Host == rhs.Host &&
                    lhs.Port == rhs.Port);

            public override int GetHashCode(Endpoint endpoint) =>
                HashCode.Combine(endpoint.Protocol, endpoint.Host, endpoint.Port);
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
