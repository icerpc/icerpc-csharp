// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text;

namespace IceRpc;

/// <summary>A server address specifies the address of the server-end of an ice or icerpc connection: a server listens
/// on a server address and a client establishes a connection to a server address.</summary>
// The properties of this struct are sorted in URI order.
[TypeConverter(typeof(ServerAddressTypeConverter))]
public readonly record struct ServerAddress
{
    /// <summary>Gets the protocol of this server address.</summary>
    /// <value>Either <see cref="Protocol.IceRpc" /> or <see cref="Protocol.Ice" />.</value>
    public Protocol Protocol { get; }

    /// <summary>Gets or initializes the host.</summary>
    /// <value>The host of this server address. Defaults to <c>::0</c> meaning that the server will listen on all the
    /// network interfaces. This default value is parsed into <see cref="IPAddress.IPv6Any" />.</value>
    public string Host
    {
        get => _host;

        init
        {
            if (Uri.CheckHostName(value) == UriHostNameType.Unknown)
            {
                throw new ArgumentException($"Cannot set {nameof(Host)} to '{value}'.", nameof(value));
            }
            _host = value;
            OriginalUri = null; // new host invalidates OriginalUri
        }
    }

    /// <summary>Gets or initializes the port number.</summary>
    /// <value>The port number of this server address. Defaults to <see cref="Protocol.DefaultPort" />.</value>
    public ushort Port
    {
        get => _port;

        init
        {
            _port = value;
            OriginalUri = null; // new port invalidates OriginalUri
        }
    }

    /// <summary>Gets or initializes the transport.</summary>
    /// <value>The name of the transport, or <see langword="null"/> if the transport is unspecified. Defaults to
    /// <see langword="null"/>.</value>
    public string? Transport
    {
        get => _transport;

        init
        {
            _transport = value is null || (ServiceAddress.IsValidParamValue(value) && value.Length > 0) ? value :
                throw new ArgumentException($"The value '{value}' is not valid transport name", nameof(value));
            OriginalUri = null; // new transport invalidates OriginalUri
        }
    }

    /// <summary>Gets or initializes transport-specific parameters.</summary>
    /// <value>The server address parameters. Defaults to <see cref="ImmutableDictionary{TKey, TValue}.Empty"
    /// />.</value>
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
                throw new ArgumentException("Invalid parameters.", nameof(value), ex);
            }
            _params = value;
            OriginalUri = null; // new params invalidates OriginalUri
        }
    }

    /// <summary>Gets the URI used to create this server address.</summary>
    /// <value>The <see cref="Uri" /> of this server address if it was constructed from an URI; <see langword="null"/>
    /// otherwise.</value>
    public Uri? OriginalUri { get; private init; }

    private readonly string _host = "::0";
    private readonly ImmutableDictionary<string, string> _params = ImmutableDictionary<string, string>.Empty;
    private readonly ushort _port;
    private readonly string? _transport;

    /// <summary>Constructs a server address with default values.</summary>
    public ServerAddress()
        : this(Protocol.IceRpc)
    {
    }

    /// <summary>Constructs a server address from a supported protocol.</summary>
    /// <param name="protocol">The protocol.</param>
    public ServerAddress(Protocol protocol)
    {
        Protocol = protocol;
        _port = Protocol.DefaultPort;
        _transport = null;
        OriginalUri = null;
    }

    /// <summary>Constructs a server address from a <see cref="Uri" />.</summary>
    /// <param name="uri">An absolute URI.</param>
    /// <exception cref="ArgumentException">Thrown if the <paramref name="uri" /> is not an absolute URI, or if its
    /// scheme is not a supported protocol, or if it has a non-empty path or fragment, or if it has an empty host,
    /// or if its query can't be parsed or if it has an alt-server query parameter.</exception>
    public ServerAddress(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("Cannot create a server address from a relative URI.", nameof(uri));
        }

        Protocol = Protocol.TryParse(uri.Scheme, out Protocol? protocol) ? protocol :
            throw new ArgumentException($"Cannot create a server address with protocol '{uri.Scheme}'", nameof(uri));

        _host = uri.IdnHost;
        if (_host.Length == 0)
        {
            throw new ArgumentException("Cannot create a server address with an empty host.", nameof(uri));
        }

        _port = uri.Port == -1 ? Protocol.DefaultPort : checked((ushort)uri.Port);

        if (uri.UserInfo.Length > 0)
        {
            throw new ArgumentException("Cannot create a server address with a user info.", nameof(uri));
        }

        if (uri.AbsolutePath.Length > 1)
        {
            throw new ArgumentException("Cannot create a server address with a path.", nameof(uri));
        }

        if (uri.Fragment.Length > 0)
        {
            throw new ArgumentException("Cannot create a server address with a fragment.", nameof(uri));
        }

        try
        {
            (_params, string? altServerValue, _transport) = uri.ParseQuery();

            if (altServerValue is not null)
            {
                throw new ArgumentException(
                    "Cannot create a server address with an alt-server query parameter.",
                    nameof(uri));
            }
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Cannot parse query of server address URI.", nameof(uri), exception);
        }

        OriginalUri = uri;
    }

    /// <summary>Checks if this server address is equal to another server address.</summary>
    /// <param name="other">The other server address.</param>
    /// <returns><see langword="true" /> when the two server addresses have the same properties, including the same
    /// parameters; <see langword="false" /> otherwise.</returns>
    public bool Equals(ServerAddress other) =>
        Protocol == other.Protocol &&
        Host == other.Host &&
        Port == other.Port &&
        Transport == other.Transport &&
        Params.DictionaryEqual(other.Params);

    /// <summary>Computes the hash code for this server address.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Protocol, Host, Port, Transport, Params.Count);

    /// <summary>Converts this server address into a string.</summary>
    /// <returns>The string representation of this server address.</returns>
    public override string ToString() =>
        OriginalUri?.ToString() ?? new StringBuilder().AppendServerAddress(this).ToString();

    /// <summary>Converts this server address into a URI.</summary>
    /// <returns>The URI.</returns>
    public Uri ToUri() => OriginalUri ?? new Uri(ToString(), UriKind.Absolute);

    /// <summary>Constructs a server address from a protocol, a host, a port and parsed parameters, without parameter
    /// validation.</summary>
    /// <remarks>This constructor is used by <see cref="ServiceAddress" /> for its main server address and by the Slice
    /// decoder for Slice1 server addresses.</remarks>
    internal ServerAddress(
        Protocol protocol,
        string host,
        ushort port,
        string? transport,
        ImmutableDictionary<string, string> serverAddressParams)
    {
        Protocol = protocol;
        _host = host;
        _port = port;
        _transport = transport;
        _params = serverAddressParams;
        OriginalUri = null;
    }
}

/// <summary>Equality comparer for <see cref="ServerAddress" />.</summary>
public abstract class ServerAddressComparer : EqualityComparer<ServerAddress>
{
    /// <summary>Gets a server address comparer that compares all server address properties, except a transport mismatch
    /// where the transport of one of the server addresses is null results in equality.</summary>
    /// <value>A <see cref="ServerAddressComparer" /> instance that compares server address properties with the
    /// exception of the <see cref="ServerAddress.Transport" /> properties which are only compared if non-null.</value>
    public static ServerAddressComparer OptionalTransport { get; } = new OptionalTransportServerAddressComparer();

    private class OptionalTransportServerAddressComparer : ServerAddressComparer
    {
        public override bool Equals(ServerAddress lhs, ServerAddress rhs) =>
            lhs.Protocol == rhs.Protocol &&
            lhs.Host == rhs.Host &&
            lhs.Port == rhs.Port &&
            (lhs.Transport == rhs.Transport || lhs.Transport is null || rhs.Transport is null) &&
            lhs.Params.DictionaryEqual(rhs.Params);

        public override int GetHashCode(ServerAddress serverAddress) =>
            HashCode.Combine(serverAddress.Protocol, serverAddress.Host, serverAddress.Port, serverAddress.Params.Count);
    }
}

/// <summary>The server address type converter specifies how to convert a string to a serverAddress. It's used by
/// sub-systems such as the Microsoft ConfigurationBinder to bind string values to ServerAddress properties.</summary>
public class ServerAddressTypeConverter : TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc/>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string valueStr ? new ServerAddress(new Uri(valueStr)) : base.ConvertFrom(context, culture, value);
}
