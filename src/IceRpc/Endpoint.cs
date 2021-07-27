// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
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

    /// <summary>This class provides extension methods for <see cref="Endpoint"/>.</summary>
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
                sb.Append("ice+");
                sb.Append(endpoint.Transport);
                sb.Append("://");
            }

            if (endpoint.Host.Contains(':', StringComparison.Ordinal))
            {
                sb.Append('[');
                sb.Append(endpoint.Host);
                sb.Append(']');
            }
            else
            {
                sb.Append(endpoint.Host);
            }

            if (endpoint.Port != IceUriParser.DefaultPort)
            {
                sb.Append(':');
                sb.Append(endpoint.Port.ToString(CultureInfo.InvariantCulture));
            }

            if (path.Length > 0)
            {
                sb.Append(path);
            }

            bool firstOption = true;

            if (endpoint.Protocol != Protocol.Ice2)
            {
                AppendQueryOption();
                sb.Append("protocol=");
                sb.Append(endpoint.Protocol.GetName());
            }
            foreach ((string name, string value) in endpoint.ExternalParams)
            {
                AppendQueryOption();
                sb.Append(name);
                sb.Append('=');
                sb.Append(value);
            }
            foreach ((string name, string value) in endpoint.LocalParams)
            {
                AppendQueryOption();
                sb.Append(name);
                sb.Append('=');
                sb.Append(value);
            }
            return sb;

            void AppendQueryOption()
            {
                if (firstOption)
                {
                    sb.Append('?');
                    firstOption = false;
                }
                else
                {
                    sb.Append(optionSeparator);
                }
            }
        }

        /// <summary>Converts this endpoint into an endpoint data. This method is called when encoding an endpoint.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>An endpoint data with all the properties of this endpoint except
        /// <see cref="Endpoint.LocalParams"/>.</returns>
        public static EndpointData ToEndpointData(this Endpoint endpoint) =>
            new(endpoint.Protocol, endpoint.Transport, endpoint.Host, endpoint.Port, endpoint.ExternalParams);
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

    /// <summary>This class provides extension methods for <see cref="EndpointData"/>.</summary>
    public static class EndpointDataExtensions
    {
        /// <summary>Converts an endpoint data into an endpoint. This method is used when decoding an endpoint.
        /// </summary>
        /// <param name="endpointData">The endpoint data struct.</param>
        /// <returns>The new endpoint.</returns>
        public static Endpoint ToEndpoint(this in EndpointData endpointData) =>
            new(endpointData.Protocol,
                string.IsInterned(endpointData.Transport) ?? endpointData.Transport,
                endpointData.Host,
                endpointData.Port,
                endpointData.Params.ToImmutableList(),
                ImmutableList<EndpointParam>.Empty);
    }

    // The main EndpointParam is generated by the Slice compiler.
    public partial struct EndpointParam
    {
        /// <summary>Deconstructs an endpoint parameter.</summary>
        /// <param name="name">The deconstructed name.</param>
        /// <param name="name">The deconstructed value.</param>
        public void Deconstruct(out string name, out string value)
        {
            name = Name;
            value = Value;
        }
    }

}
