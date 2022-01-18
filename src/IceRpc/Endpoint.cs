// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
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
        public Protocol Protocol
        {
            get => _protocol;
            init
            {
                if (!value.IsSupported)
                {
                    throw new ArgumentException(
                        $"cannot set {nameof(Protocol)} to a non-supported protocol",
                        nameof(Protocol));
                }
                _protocol = value;
                OriginalUri = null; // new protocol invalidates OriginalUri
            }
        }

        /// <summary>The host name or address.</summary>
        public string Host
        {
            get => _host;
            init
            {
                if (Uri.CheckHostName(value) == UriHostNameType.Unknown)
                {
                    throw new ArgumentException($"cannot set {nameof(Host)} to '{value}'", nameof(Host));
                }
                _host = value;
                OriginalUri = null; // new host invalidates OriginalUri
            }
        }

        /// <summary>The port number.</summary>
        public ushort Port
        {
            get => _port;
            init
            {
                _port = value;
                OriginalUri = null; // new port invalidates OriginalUri
            }
        }

        /// <summary>Transport-specific parameters.</summary>
        public ImmutableDictionary<string, string> Params
        {
            get => _params;
            init
            {
                Proxy.CheckParams(value, nameof(Params));
                _params = value;
                OriginalUri = null; // new params invalidates OriginalUri
            }
        }

        /// <summary>Returns the URI used to create this endpoint, if this endpoint was created from a URI.</summary>
        public Uri? OriginalUri { get; private init; }

        private string _host;
        private ImmutableDictionary<string, string> _params = ImmutableDictionary<string, string>.Empty;
        private ushort _port;
        private Protocol _protocol;

        /// <summary>Converts a string into an endpoint implicitly using <see cref="FromString"/>.</summary>
        /// <param name="s">The string representation of the endpoint.</param>
        /// <returns>The new endpoint.</returns>
        /// <exception cref="FormatException">Thrown when <paramref name="s"/> is not a valid endpoint URI string.
        /// </exception>
        public static implicit operator Endpoint(string s) => FromString(s);

        /// <summary>Creates an endpoint from a URI string.</summary>
        /// <param name="s">The string representation of the endpoint.</param>
        /// <returns>The new endpoint.</returns>
        /// <exception cref="FormatException">Thrown when <paramref name="s"/> is not a valid endpoint URI string.
        /// </exception>
        public static Endpoint FromString(string s)
        {
            try
            {
                return new Endpoint(new Uri(s, UriKind.Absolute));
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"'{s}' is not a valid endpoint URI", ex);
            }
        }

        /// <summary>Constructs an endpoint from a protocol and host.</summary>
        /// <param name="protocol">The protocol of this endpoint. Must be a supported protocol.</param>
        /// <param name="host">The host name. Cannot be empty.</param>
        public Endpoint(Protocol protocol, string host)
        {
            if (!protocol.IsSupported)
            {
                throw new ArgumentException($"cannot create an endpoint with protocol '{protocol}'", nameof(protocol));
            }
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                throw new ArgumentException($"cannot create an endpoint with host '{host}'", nameof(host));
            }

            _protocol = protocol;
            _host = host;
            _port = (ushort)protocol.DefaultUriPort;
        }

        /// <summary>Constructs an endpoint from a <see cref="Uri"/>.</summary>
        /// <param name="uri">An absolute URI.</param>
        /// <exception cref="ArgumentException">Thrown if the <paramref name="uri"/> is not an absolute URI, or if its
        /// scheme is not a supported protocol, or if it has a non-empty path or fragment, or if it has an empty host,
        /// or if its query can't be parsed or if it has an alt-endpoint query parameter.</exception>
        /// <exception cref="FormatException">Thrown if the query portion of the URI cannot be parsed.</exception>
        public Endpoint(Uri uri)
        {
            if (!uri.IsAbsoluteUri)
            {
                throw new ArgumentException("cannot create an endpoint from a relative reference", nameof(uri));
            }
            _protocol = Protocol.FromString(uri.Scheme);
            if (!_protocol.IsSupported)
            {
                throw new ArgumentException($"cannot create an endpoint with protocol '{_protocol}'", nameof(uri));
            }
            _host = uri.IdnHost;
            if (_host.Length == 0)
            {
                throw new ArgumentException("cannot create an endpoint with an empty host", nameof(uri));
            }

            // bug if it throws OverflowException
            _port = checked((ushort)(uri.Port == -1 ? _protocol.DefaultUriPort : uri.Port));

            if (uri.UserInfo.Length > 0)
            {
                throw new ArgumentException("cannot create an endpoint with a user info", nameof(uri));
            }

            if (uri.AbsolutePath.Length > 1)
            {
                throw new ArgumentException("cannot create an endpoint with a path", nameof(uri));
            }

            if (uri.Fragment.Length > 0)
            {
                throw new ArgumentException("cannot create an endpoint with a fragment", nameof(uri));
            }

            (_params, string? altEndpointValue) = uri.ParseQuery();
            if (altEndpointValue != null)
            {
                throw new ArgumentException(
                    "cannot create an endpoint with an alt-endpoint query parameter",
                    nameof(uri));
            }

            OriginalUri = uri;
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
        public override string ToString() =>
            OriginalUri?.ToString() ?? new StringBuilder().AppendEndpoint(this).ToString();

        /// <summary>Converts this endpoint into a URI.</summary>
        /// <returns>The URI.</returns>
        public Uri ToUri() => OriginalUri ?? new Uri(ToString(), UriKind.Absolute);

        /// <summary>Constructs an endpoint from a <see cref="Uri"/>, a protocol and parsed parameters.</summary>
        /// <remarks>This constructor is used by <see cref="Proxy"/> for its main endpoint.</remarks>
        internal Endpoint(Uri uri, Protocol protocol, ImmutableDictionary<string, string> endpointParams)
        {
            Debug.Assert(uri.Scheme == protocol.Name);
            Debug.Assert(uri.IsAbsoluteUri);
            Debug.Assert(protocol.IsSupported);

            _protocol = protocol;
            _host = uri.IdnHost;
            if (_host.Length == 0)
            {
                throw new ArgumentException("cannot create an endpoint with an empty host", nameof(uri));
            }

            _port = checked((ushort)(uri.Port == -1 ? _protocol.DefaultUriPort : uri.Port));
            _params = endpointParams;

            if (uri.UserInfo.Length > 0)
            {
                throw new ArgumentException("cannot create an endpoint with a user info", nameof(uri));
            }

            // ignore path, fragment
            // don't set OriginalUri as uri typically has a path, alt-endpoint and more.
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
