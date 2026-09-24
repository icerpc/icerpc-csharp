// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace IceRpc;

/// <summary>A server address specifies the address of the server-end of an ice or icerpc connection: a server listens
/// on a server address and a client establishes a connection to a server address. It is a union of <see cref="Ice" />
/// and <see cref="IceRpc" />, the server address of each protocol.</summary>
// The properties of this struct are sorted in URI order.
[TypeConverter(typeof(ServerAddressTypeConverter))]
[Union]
public readonly record struct ServerAddress : IUnion
{
    /// <summary>The address of the server-end of an ice connection.</summary>
    // The properties of this struct are sorted in URI order.
    public readonly record struct Ice
    {
        /// <summary>Gets or initializes the host.</summary>
        /// <value>The host of this server address. Defaults to <c>::0</c> meaning that the server will listen on all
        /// the network interfaces. This default value is parsed into <see cref="IPAddress.IPv6Any" />.</value>
        /// <remarks>When you initialize this property with a bracketed IPv6 address such as <c>[::1]</c>, the brackets
        /// are stripped: the property value is the IPv6 address without brackets.</remarks>
        public string Host
        {
            get => _host;
            init => _host = CheckHost(value);
        }

        /// <summary>Gets or initializes the port number.</summary>
        /// <value>The port number of this server address. Defaults to <c>4061</c>.</value>
        public ushort Port { get; init; } = Protocol.Ice.DefaultPort;

        /// <summary>Gets or initializes the transport.</summary>
        /// <value>The name of the transport, or <see langword="null" /> if the transport is unspecified. Defaults to
        /// <see langword="null" />.</value>
        public string? Transport
        {
            get => _transport;
            init => _transport = CheckTransport(value);
        }

        /// <summary>Gets or initializes the transport-specific parameters, such as <c>t</c> and <c>z</c> for tcp and
        /// ssl.</summary>
        /// <value>The server address parameters. Defaults to
        /// <see cref="ImmutableDictionary{TKey, TValue}.Empty" />.</value>
        public ImmutableDictionary<string, string> Params
        {
            get => _params;
            init => _params = CheckParams(value);
        }

        private readonly string _host = DefaultHost;
        private readonly ImmutableDictionary<string, string> _params = ImmutableDictionary<string, string>.Empty;
        private readonly string? _transport;

        /// <summary>Constructs an ice server address with default values.</summary>
        public Ice()
        {
        }

        /// <summary>Constructs an ice server address from a URI.</summary>
        /// <param name="uri">An absolute URI with the <c>ice</c> scheme, a host and no path, fragment or
        /// <c>alt-server</c> parameter.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="uri" /> does not meet these requirements.
        /// </exception>
        public Ice(Uri uri) => (_host, Port, _transport, _params) = ParseUri(uri, Protocol.Ice);

        /// <summary>Checks if this server address is equal to another server address.</summary>
        /// <param name="other">The other server address.</param>
        /// <returns><see langword="true" /> when the two server addresses have the same properties, including the same
        /// parameters; otherwise, <see langword="false" />.</returns>
        public bool Equals(Ice other) =>
            Host == other.Host &&
            Port == other.Port &&
            Transport == other.Transport &&
            Params.DictionaryEqual(other.Params);

        /// <summary>Computes the hash code for this server address.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => HashCode.Combine(Host, Port, Transport, Params.Count);

        /// <summary>Converts this server address into a string.</summary>
        /// <returns>The URI of this server address.</returns>
        public override string ToString() => new StringBuilder().AppendServerAddress(this).ToString();

        /// <summary>Converts this server address into a URI.</summary>
        /// <returns>The URI.</returns>
        public Uri ToUri() => new(ToString(), UriKind.Absolute);

        /// <summary>Constructs an ice server address without validation.</summary>
        internal Ice(
            string host,
            ushort port,
            string? transport,
            ImmutableDictionary<string, string> serverAddressParams)
        {
            _host = host;
            Port = port;
            _transport = transport;
            _params = serverAddressParams;
        }
    }

    /// <summary>The address of the server-end of an icerpc connection.</summary>
    // The properties of this struct are sorted in URI order.
    public readonly record struct IceRpc
    {
        /// <summary>Gets or initializes the host.</summary>
        /// <value>The host of this server address. Defaults to <c>::0</c> meaning that the server will listen on all
        /// the network interfaces. This default value is parsed into <see cref="IPAddress.IPv6Any" />.</value>
        /// <remarks>When you initialize this property with a bracketed IPv6 address such as <c>[::1]</c>, the brackets
        /// are stripped: the property value is the IPv6 address without brackets.</remarks>
        public string Host
        {
            get => _host;
            init => _host = CheckHost(value);
        }

        /// <summary>Gets or initializes the port number.</summary>
        /// <value>The port number of this server address. Defaults to <c>4062</c>.</value>
        public ushort Port { get; init; } = Protocol.IceRpc.DefaultPort;

        /// <summary>Gets or initializes the transport.</summary>
        /// <value>The name of the transport, or <see langword="null" /> if the transport is unspecified. Defaults to
        /// <see langword="null" />.</value>
        public string? Transport
        {
            get => _transport;
            init => _transport = CheckTransport(value);
        }

        private readonly string _host = DefaultHost;
        private readonly string? _transport;

        /// <summary>Constructs an icerpc server address with default values.</summary>
        public IceRpc()
        {
        }

        /// <summary>Constructs an icerpc server address from a URI.</summary>
        /// <param name="uri">An absolute URI with the <c>icerpc</c> scheme, a host and no path, fragment or
        /// parameter other than <c>transport</c>.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="uri" /> does not meet these requirements.
        /// </exception>
        public IceRpc(Uri uri)
        {
            (_host, Port, _transport, ImmutableDictionary<string, string> serverAddressParams) =
                ParseUri(uri, Protocol.IceRpc);

            if (serverAddressParams.Count > 0)
            {
                throw new ArgumentException(
                    "Cannot create an icerpc server address with a parameter other than transport.",
                    nameof(uri));
            }
        }

        /// <summary>Converts this server address into a string.</summary>
        /// <returns>The URI of this server address.</returns>
        public override string ToString() => new StringBuilder().AppendServerAddress(this).ToString();

        /// <summary>Converts this server address into a URI.</summary>
        /// <returns>The URI.</returns>
        public Uri ToUri() => new(ToString(), UriKind.Absolute);

        /// <summary>Constructs an icerpc server address without validation.</summary>
        internal IceRpc(string host, ushort port, string? transport)
        {
            _host = host;
            Port = port;
            _transport = transport;
        }
    }

    /// <summary>Gets the protocol of this server address.</summary>
    /// <value><see cref="Protocol.Ice" /> for an <see cref="Ice" /> server address and <see cref="Protocol.IceRpc" />
    /// for an <see cref="IceRpc" /> server address.</value>
    /// <exception cref="InvalidOperationException">Thrown when this server address holds no variant.</exception>
    public Protocol Protocol =>
        _protocol ?? throw new InvalidOperationException("The default server address has no protocol.");

    /// <summary>Gets the host.</summary>
    /// <value>The host of the variant.</value>
    public string Host => this switch
    {
        Ice ice => ice.Host,
        IceRpc icerpc => icerpc.Host,
    };

    /// <summary>Gets the port number.</summary>
    /// <value>The port number of the variant.</value>
    public ushort Port => this switch
    {
        Ice ice => ice.Port,
        IceRpc icerpc => icerpc.Port,
    };

    /// <summary>Gets the transport.</summary>
    /// <value>The transport of the variant, or <see langword="null" /> if the transport is unspecified.</value>
    public string? Transport => this switch
    {
        Ice ice => ice.Transport,
        IceRpc icerpc => icerpc.Transport,
    };

    /// <summary>Gets the transport-specific parameters of this server address.</summary>
    /// <value>The parameters of an <see cref="Ice" /> server address, or an empty dictionary for an
    /// <see cref="IceRpc" /> server address.</value>
    public ImmutableDictionary<string, string> Params =>
        _protocol == Protocol.Ice ? _ice.Params : ImmutableDictionary<string, string>.Empty;

    /// <summary>Gets a value indicating whether this server address holds a variant.</summary>
    /// <value><see langword="false" /> for the default value of this struct; otherwise, <see langword="true" />.
    /// </value>
    public bool HasValue => _protocol is not null;

    /// <summary>Gets the variant as an object.</summary>
    /// <value>A boxed <see cref="Ice" /> or <see cref="IceRpc" />, or <see langword="null" /> when this server
    /// address holds no variant.</value>
    public object? Value =>
        _protocol == Protocol.Ice ? _ice :
        _protocol == Protocol.IceRpc ? _icerpc :
        null;

    private const string DefaultHost = "::0";

    // The printable ASCII character range is x20 (space) to x7E inclusive. Space is an invalid character in a
    // parameter name or value, in addition to the invalid characters in the _notValidInXXX search values.
    private const char FirstValidChar = '\x21';
    private const char LastValidChar = '\x7E';

    private static readonly SearchValues<char> _notValidInParamName = SearchValues.Create("\"<>#&=\\^`{|}");
    private static readonly SearchValues<char> _notValidInParamValue = SearchValues.Create("\"<>#&\\^`{|}");

    private readonly Ice _ice;
    private readonly IceRpc _icerpc;
    private readonly Protocol? _protocol;

    /// <summary>Constructs an icerpc server address with default values.</summary>
    public ServerAddress()
        : this(new IceRpc())
    {
    }

    /// <summary>Constructs a server address from an ice server address.</summary>
    /// <param name="value">The ice server address.</param>
    public ServerAddress(Ice value)
    {
        _ice = value;
        _protocol = Protocol.Ice;
    }

    /// <summary>Constructs a server address from an icerpc server address.</summary>
    /// <param name="value">The icerpc server address.</param>
    public ServerAddress(IceRpc value)
    {
        _icerpc = value;
        _protocol = Protocol.IceRpc;
    }

    /// <summary>Creates a server address from a URI.</summary>
    /// <param name="uri">An absolute URI whose scheme is a supported protocol.</param>
    /// <returns>An <see cref="Ice" /> or <see cref="IceRpc" /> server address, depending on the scheme of
    /// <paramref name="uri" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="uri" /> is not a valid server address URI.
    /// </exception>
    public static ServerAddress FromUri(Uri uri) =>
        uri.IsAbsoluteUri && Protocol.TryParse(uri.Scheme, out Protocol? protocol) ?
            (protocol == Protocol.Ice ? new Ice(uri) : new IceRpc(uri)) :
            throw new ArgumentException($"Cannot create a server address from URI '{uri}'.", nameof(uri));

    /// <summary>Checks if this server address is equal to another server address.</summary>
    /// <param name="other">The other server address.</param>
    /// <returns><see langword="true" /> when the two server addresses hold equal variants, or both hold no variant;
    /// otherwise, <see langword="false" />.</returns>
    public bool Equals(ServerAddress other) =>
        _protocol == other._protocol &&
        (_protocol == Protocol.Ice ? _ice.Equals(other._ice) :
            _protocol == Protocol.IceRpc ? _icerpc.Equals(other._icerpc) :
            true);

    /// <summary>Computes the hash code for this server address.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        _protocol == Protocol.Ice ? _ice.GetHashCode() :
        _protocol == Protocol.IceRpc ? _icerpc.GetHashCode() :
        0;

    /// <summary>Converts this server address into a string.</summary>
    /// <returns>The URI of the variant, or an empty string when this server address holds no variant.</returns>
    public override string ToString() => Value?.ToString() ?? "";

    /// <summary>Converts this server address into a URI.</summary>
    /// <returns>The URI of the variant.</returns>
    public Uri ToUri() => new(ToString(), UriKind.Absolute);

    /// <summary>Gets the <see cref="Ice" /> variant.</summary>
    /// <param name="value">The ice server address.</param>
    /// <returns><see langword="true" /> when this server address holds an <see cref="Ice" /> variant; otherwise,
    /// <see langword="false" />.</returns>
    public bool TryGetValue(out Ice value)
    {
        value = _ice;
        return _protocol == Protocol.Ice;
    }

    /// <summary>Gets the <see cref="IceRpc" /> variant.</summary>
    /// <param name="value">The icerpc server address.</param>
    /// <returns><see langword="true" /> when this server address holds an <see cref="IceRpc" /> variant; otherwise,
    /// <see langword="false" />.</returns>
    public bool TryGetValue(out IceRpc value)
    {
        value = _icerpc;
        return _protocol == Protocol.IceRpc;
    }

    /// <summary>Returns a copy of this server address with a new transport.</summary>
    /// <param name="transport">The name of the transport, or <see langword="null" /> to leave the transport
    /// unspecified.</param>
    /// <returns>A server address of the same variant with the new transport.</returns>
    public ServerAddress WithTransport(string? transport) => this switch
    {
        Ice ice => ice with { Transport = transport },
        IceRpc icerpc => icerpc with { Transport = transport },
    };

    /// <summary>Returns a copy of this server address with a new port.</summary>
    internal ServerAddress WithPort(ushort port) => this switch
    {
        Ice ice => ice with { Port = port },
        IceRpc icerpc => icerpc with { Port = port },
    };

    private static string CheckHost(string host) =>
        Uri.CheckHostName(host) == UriHostNameType.Unknown ?
            throw new ArgumentException($"Cannot set {nameof(Host)} to '{host}'.", nameof(host)) :
            // A value that starts with '[' is necessarily a well-formed bracketed IPv6 address: for any other value
            // with brackets, including mismatched brackets, CheckHostName returns Unknown. We store the address
            // without the brackets, like the Uri constructor does.
            host.StartsWith('[', StringComparison.Ordinal) ? host[1..^1] : host;

    /// <summary>Checks if the parameters have properly escaped names and values.</summary>
    /// <remarks>A dictionary returned by <see cref="UriExtensions.ParseQuery" /> is properly escaped.</remarks>
    private static ImmutableDictionary<string, string> CheckParams(ImmutableDictionary<string, string> @params)
    {
        foreach ((string name, string value) in @params)
        {
            // A valid name is not empty, not alt-server nor transport, and contains only unreserved characters, '%'
            // and reserved characters other than '#', '&' and '='.
            if (name.Length == 0 || name == "alt-server" || name == "transport" || !IsValid(name, _notValidInParamName))
            {
                throw new ArgumentException($"Invalid parameter name '{name}'.", nameof(@params));
            }
            if (!IsValidParamValue(value))
            {
                throw new ArgumentException($"Invalid parameter value '{value}'.", nameof(@params));
            }
        }
        return @params;
    }

    private static string? CheckTransport(string? transport) =>
        transport is null || (transport.Length > 0 && IsValidParamValue(transport)) ? transport :
            throw new ArgumentException($"The value '{transport}' is not a valid transport name.", nameof(transport));

    private static bool IsValid(string s, SearchValues<char> invalidChars)
    {
        ReadOnlySpan<char> span = s.AsSpan();
        return span.IndexOfAnyExceptInRange(FirstValidChar, LastValidChar) == -1 && span.IndexOfAny(invalidChars) == -1;
    }

    /// <summary>Checks if a value contains only unreserved characters, <c>%</c>, and reserved characters other than
    /// <c>#</c> and <c>&#38;</c>.</summary>
    private static bool IsValidParamValue(string value) => IsValid(value, _notValidInParamValue);

    /// <summary>Parses a server address URI into its components.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="uri" /> is not an absolute URI with the scheme
    /// of <paramref name="protocol" />, or when it has a user info, a non-empty path, a fragment, an empty host, or a
    /// query that can't be parsed or has an alt-server parameter.</exception>
    private static (string Host, ushort Port, string? Transport, ImmutableDictionary<string, string> Params) ParseUri(
        Uri uri,
        Protocol protocol)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != protocol.Name)
        {
            throw new ArgumentException($"Cannot create an {protocol} server address from URI '{uri}'.", nameof(uri));
        }

        string host = uri.IdnHost;
        if (host.Length == 0)
        {
            throw new ArgumentException("Cannot create a server address with an empty host.", nameof(uri));
        }

        ushort port = uri.Port == -1 ? protocol.DefaultPort : checked((ushort)uri.Port);

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
            (ImmutableDictionary<string, string> queryParams, string? altServerValue, string? transport) =
                uri.ParseQuery();

            if (altServerValue is not null)
            {
                throw new ArgumentException(
                    "Cannot create a server address with an alt-server query parameter.",
                    nameof(uri));
            }
            return (host, port, transport, queryParams);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Cannot parse query of server address URI.", nameof(uri), exception);
        }
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
        public override bool Equals(ServerAddress lhs, ServerAddress rhs)
        {
            if (!lhs.HasValue || !rhs.HasValue)
            {
                return lhs.HasValue == rhs.HasValue;
            }

            // An unspecified transport matches any transport.
            return lhs.Transport is null || rhs.Transport is null ?
                lhs.WithTransport(null) == rhs.WithTransport(null) :
                lhs == rhs;
        }

        public override int GetHashCode(ServerAddress serverAddress) =>
            serverAddress.HasValue ? serverAddress.WithTransport(null).GetHashCode() : 0;
    }
}

/// <summary>The server address type converter specifies how to convert a string to a ServerAddress. It's used by
/// sub-systems such as the Microsoft ConfigurationBinder to bind string values to ServerAddress properties.</summary>
public class ServerAddressTypeConverter : TypeConverter
{
    /// <summary>Returns whether this converter can convert an object of the given type into a
    /// <see cref="ServerAddress"/> value, using the specified context.</summary>
    /// <param name="context">An <see cref="ITypeDescriptorContext"/> that provides a format context.</param>
    /// <param name="sourceType">A <see cref="Type"/> that represents the type you want to convert from.</param>
    /// <returns><see langword="true"/>if this converter can perform the conversion; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <summary>Converts the given object into a <see cref="ServerAddress"/> value, using the specified context and
    /// culture information.</summary>
    /// <param name="context">An <see cref="ITypeDescriptorContext"/> that provides a format context.</param>
    /// <param name="culture">The <see cref="CultureInfo"/> to use as the current culture.</param>
    /// <param name="value">The <see cref="object "/> to convert.</param>
    /// <returns>An <see cref="object "/> that represents the converted <see cref="ServerAddress"/> value.</returns>
    /// <remarks><see cref="TypeConverter"/>.</remarks>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string valueStr ? ServerAddress.FromUri(new Uri(valueStr)) : base.ConvertFrom(context, culture, value);
}
