// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace IceRpc;

/// <summary>An endpoint specifies the address of the server-end of an ice or icerpc connection: a server listens on an
/// endpoint and a client establishes a connection to a given endpoint.</summary>
[TypeConverter(typeof(EndpointTypeConverter))]
public readonly record struct Endpoint
{
    /// <summary>Gets the endpoint's protocol.</summary>
    /// <value>A supported protocol - either <see cref="Protocol.IceRpc"/> or <see cref="Protocol.Ice"/>.</value>
    public Protocol Protocol { get; }

    /// <summary>Gets the endpoint's host name or address.</summary>
    public string Host
    {
        get => _host;

        init
        {
            if (Uri.CheckHostName(value) == UriHostNameType.Unknown)
            {
                throw new ArgumentException($"cannot set {nameof(Host)} to '{value}'", nameof(value));
            }
            _host = value;
            OriginalUri = null; // new host invalidates OriginalUri
        }
    }

    /// <summary>Gets the endpoint's port number.</summary>
    public ushort Port
    {
        get => _port;

        init
        {
            _port = value;
            OriginalUri = null; // new port invalidates OriginalUri
        }
    }

    /// <summary>Gets the endpoint's transport-specific parameters.</summary>
    public ImmutableDictionary<string, string> Params
    {
        get => _params;

        init
        {
            try
            {
                ServiceAddress.CheckParams(value);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException($"invalid parameters", nameof(Params), ex);
            }
            _params = value;
            OriginalUri = null; // new params invalidates OriginalUri
        }
    }

    /// <summary>Gets the URI used to create this endpoint, if this endpoint was created from a URI.</summary>
    public Uri? OriginalUri { get; private init; }

    /// <summary>Gets the transport of this endpoint, or null if the transport not specified.</summary>
    public string? Transport
    {
        get => _transport;

        init => _transport = value is null || (ServiceAddress.IsValidParamValue(value) && value.Length > 0) ? value :
            throw new ArgumentException($"`{value}` is not valid transport name", nameof(value));
    }

    private readonly string _host = "::0";
    private readonly ImmutableDictionary<string, string> _params = ImmutableDictionary<string, string>.Empty;
    private readonly ushort _port;
    private readonly string? _transport;

    /// <summary>Constructs an endpoint with default values.</summary>
    public Endpoint()
        : this(Protocol.IceRpc)
    {
    }

    /// <summary>Constructs an endpoint from a protocol.</summary>
    /// <param name="protocol">The endpoint's protocol.</param>
    public Endpoint(Protocol protocol)
    {
        if (!protocol.IsSupported)
        {
            throw new ArgumentException(
                "cannot create an endpoint with a non-supported protocol",
                nameof(protocol));
        }

        Protocol = protocol;
        _port = (ushort)Protocol.DefaultUriPort;
        _transport = null;
        OriginalUri = null;
    }

    /// <summary>Constructs an endpoint from a <see cref="Uri"/>.</summary>
    /// <param name="uri">An absolute URI.</param>
    /// <exception cref="ArgumentException">Thrown if the <paramref name="uri"/> is not an absolute URI, or if its
    /// scheme is not a supported protocol, or if it has a non-empty path or fragment, or if it has an empty host,
    /// or if its query can't be parsed or if it has an alt-endpoint query parameter.</exception>
    public Endpoint(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("cannot create an endpoint from a relative URI", nameof(uri));
        }
        Protocol = Protocol.FromString(uri.Scheme);
        if (!Protocol.IsSupported)
        {
            throw new ArgumentException($"cannot create an endpoint with protocol '{Protocol}'", nameof(uri));
        }
        _host = uri.IdnHost;
        if (_host.Length == 0)
        {
            throw new ArgumentException("cannot create an endpoint with an empty host", nameof(uri));
        }

        // bug if it throws OverflowException
        _port = checked((ushort)(uri.Port == -1 ? Protocol.DefaultUriPort : uri.Port));

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

        try
        {
            (_params, string? altEndpointValue, _transport) = uri.ParseQuery();

            if (altEndpointValue is not null)
            {
                throw new ArgumentException(
                    "cannot create an endpoint with an alt-endpoint query parameter",
                    nameof(uri));
            }
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("cannot parse query of endpoint URI", nameof(uri), exception);
        }

        OriginalUri = uri;
    }

    /// <summary>Checks if this endpoint is equal to another endpoint.</summary>
    /// <param name="other">The other endpoint.</param>
    /// <returns><c>true</c>when the two endpoints have the same properties, including the same parameters (in the
    /// same order); otherwise, <c>false</c>.</returns>
    public bool Equals(Endpoint other) =>
        Protocol == other.Protocol &&
        Host == other.Host &&
        Port == other.Port &&
        Transport == other.Transport &&
        Params.DictionaryEqual(other.Params);

    /// <summary>Computes the hash code for this endpoint.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Protocol, Host, Port, Transport, Params.Count);

    /// <summary>Converts this endpoint into a string.</summary>
    /// <returns>The string representation of this endpoint.</returns>
    public override string ToString() =>
        OriginalUri?.ToString() ?? new StringBuilder().AppendEndpoint(this).ToString();

    /// <summary>Converts this endpoint into a URI.</summary>
    /// <returns>The URI.</returns>
    public Uri ToUri() => OriginalUri ?? new Uri(ToString(), UriKind.Absolute);

    /// <summary>Constructs an endpoint from a protocol, a host, a port and parsed parameters, without parameter
    /// validation.</summary>
    /// <remarks>This constructor is used by <see cref="ServiceAddress"/> for its main endpoint and by the Slice decoder
    /// for Slice1 endpoints.</remarks>
    internal Endpoint(
        Protocol protocol,
        string host,
        ushort port,
        string? transport,
        ImmutableDictionary<string, string> endpointParams)
    {
        Protocol = protocol;
        _host = host;
        _port = port;
        _transport = transport;
        _params = endpointParams;
        OriginalUri = null;
    }
}

/// <summary>Equality comparer for <see cref="Endpoint"/>.</summary>
public abstract class EndpointComparer : EqualityComparer<Endpoint>
{
    /// <summary>Gets an endpoint comparer that compares all endpoint properties, except a transport mismatch where the
    /// transport of one of the endpoints is null results in equality.</summary>
    public static EndpointComparer OptionalTransport { get; } = new OptionalTransportEndpointComparer();

    private class OptionalTransportEndpointComparer : EndpointComparer
    {
        public override bool Equals(Endpoint lhs, Endpoint rhs) =>
            lhs.Protocol == rhs.Protocol &&
            lhs.Host == rhs.Host &&
            lhs.Port == rhs.Port &&
            (lhs.Transport == rhs.Transport || lhs.Transport is null || rhs.Transport is null) &&
            lhs.Params.DictionaryEqual(rhs.Params);

        public override int GetHashCode(Endpoint endpoint) =>
            HashCode.Combine(endpoint.Protocol, endpoint.Host, endpoint.Port, endpoint.Params.Count);
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
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string valueStr ? new Endpoint(new Uri(valueStr)) : base.ConvertFrom(context, culture, value);
}
