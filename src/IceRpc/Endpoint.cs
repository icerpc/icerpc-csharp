// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IceRpc
{
    /// <summary>An endpoint describes...</summary>
    public sealed record Endpoint(
        Protocol Protocol,
        string Transport,
        string Host,
        ushort Port,
        ImmutableList<EndpointParam> ExternalParams,
        ImmutableList<EndpointParam> LocalParams)
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
            IceUriParser.IsEndpointUri(s) ? IceUriParser.ParseEndpointUri(s) : Ice1Parser.ParseEndpointString(s);

        /// <inheritdoc/>
        public bool Equals(Endpoint? other) =>
                   other != null &&
                   Protocol == other.Protocol &&
                   Transport == other.Transport &&
                   Host == other.Host &&
                   Port == other.Port &&
                   ExternalParams.SequenceEqual(other.ExternalParams) &&
                   LocalParams.SequenceEqual(other.LocalParams);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Protocol);
            hash.Add(Transport);
            hash.Add(Host);
            hash.Add(Port);
            hash.Add(ExternalParams.GetSequenceHashCode());
            hash.Add(LocalParams.GetSequenceHashCode());
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
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

                foreach ((string name, string value) in ExternalParams)
                {
                    sb.Append(' ');
                    sb.Append(name);
                    if (value.Length > 0)
                    {
                        sb.Append(' ');
                        sb.Append(value);
                    }
                }
                foreach ((string name, string value) in LocalParams)
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
        /// <summary>An endpoint comparer that compares all endpoint properties except the parameters and local
        /// parameters.</summary>
        public static EndpointComparer ParameterLess { get; } = new ParameterLessEndpointComparer();

        private class ParameterLessEndpointComparer : EndpointComparer
        {
            public override bool Equals(Endpoint? lhs, Endpoint? rhs) =>
                ReferenceEquals(lhs, rhs) ||
                (lhs != null && rhs != null &&
                 lhs.Protocol == rhs.Protocol &&
                 lhs.Transport == rhs.Transport &&
                 lhs.Host == rhs.Host &&
                 lhs.Port == rhs.Port);

            public override int GetHashCode(Endpoint endpoint)
            {
                var hash = new HashCode();
                hash.Add(endpoint.Protocol);
                hash.Add(endpoint.Transport);
                hash.Add(endpoint.Host);
                hash.Add(endpoint.Port);
                return hash.ToHashCode();
            }
        }
    }

    // The main EndpointParam is generated by the Slice compiler.
    public partial struct EndpointParam
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
