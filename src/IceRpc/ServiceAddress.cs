// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace IceRpc;

/// <summary>Represents the URI of a service, parsed and processed for easier consumption by invokers. It's used to
/// construct an <see cref="OutgoingRequest" />. It is a union of <see cref="Ice" /> and <see cref="IceRpc" />, the
/// service address of each protocol.</summary>
// The properties of this struct are sorted in URI order.
[TypeConverter(typeof(ServiceAddressTypeConverter))]
public union ServiceAddress(ServiceAddress.IceRpc, ServiceAddress.Ice) : IEquatable<ServiceAddress>
{
    /// <summary>The address of a service reachable with the ice protocol.</summary>
    // The properties of this class are sorted in URI order.
    public sealed record class Ice
    {
        /// <summary>Gets or initializes the main server address of this service address.</summary>
        /// <value>The main server address, an ice server address, or <see langword="null" /> if this service
        /// address has no server address.</value>
        public ServerAddress? ServerAddress
        {
            get => _serverAddress;

            init
            {
                if (value is ServerAddress serverAddress)
                {
                    if (serverAddress.Protocol != Protocol.Ice)
                    {
                        throw new ArgumentException(
                            $"The {nameof(ServerAddress)} of an ice service address must be an ice server address.",
                            nameof(value));
                    }
                    if (_adapterId.Length > 0)
                    {
                        throw new InvalidOperationException(
                            $"Cannot set {nameof(ServerAddress)} on a service address with an adapter ID.");
                    }
                }
                else if (_altServerAddresses.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot clear {nameof(ServerAddress)} when {nameof(AltServerAddresses)} is not empty.");
                }
                _serverAddress = value;
            }
        }

        /// <summary>Gets or initializes the path of this service address.</summary>
        /// <value>The path of this service address, <c>/category/name</c> or <c>/name</c> for an Ice identity.
        /// Defaults to <c>/</c>.</value>
        public string Path
        {
            get => _path;

            init
            {
                try
                {
                    CheckIcePath(value);
                }
                catch (FormatException exception)
                {
                    throw new ArgumentException("Invalid path.", nameof(value), exception);
                }
                _path = value;
            }
        }

        /// <summary>Gets or initializes the secondary server addresses of this service address.</summary>
        /// <value>The secondary server addresses of this service address, all ice server addresses. Defaults to
        /// <see cref="ImmutableList{T}.Empty" />.</value>
        public ImmutableList<ServerAddress> AltServerAddresses
        {
            get => _altServerAddresses;
            init => _altServerAddresses = CheckAltServerAddresses(value, _serverAddress, Protocol.Ice);
        }

        /// <summary>Gets or initializes the adapter ID of this service address.</summary>
        /// <value>The adapter ID, or an empty string if this service address has no adapter ID. It is always empty
        /// when <see cref="ServerAddress" /> is not <see langword="null" />. Defaults to an empty string.</value>
        public string AdapterId
        {
            get => _adapterId;

            init
            {
                if (value.Length > 0 && _serverAddress is not null)
                {
                    throw new InvalidOperationException(
                        $"Cannot set {nameof(AdapterId)} on a service address with a server address.");
                }
                _adapterId = value;
            }
        }

        /// <summary>Gets or initializes the fragment.</summary>
        /// <value>The fragment of this service address, which corresponds to the Ice facet. Defaults to an empty
        /// string.</value>
        public string Fragment
        {
            get => _fragment;

            init
            {
                try
                {
                    CheckFragment(value);
                }
                catch (FormatException exception)
                {
                    throw new ArgumentException("Invalid fragment.", nameof(value), exception);
                }
                _fragment = value;
            }
        }

        private string _adapterId = "";
        private ImmutableList<ServerAddress> _altServerAddresses = ImmutableList<ServerAddress>.Empty;
        private string _fragment = "";
        private string _path = "/";
        private ServerAddress? _serverAddress;

        /// <summary>Constructs an ice service address with default values.</summary>
        public Ice()
        {
        }

        /// <summary>Constructs an ice service address from a URI.</summary>
        /// <param name="uri">An absolute URI with the <c>ice</c> scheme, such as
        /// <c>ice://host:port/category/name?transport=tcp#facet</c> or <c>ice:/name?adapter-id=foo</c>.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="uri" /> is not a valid ice service address
        /// URI.</exception>
        public Ice(Uri uri)
        {
            (string path, ServerAddress? serverAddress, ImmutableList<ServerAddress> altServerAddresses,
                ImmutableDictionary<string, string> queryParams, string fragment) = ParseUri(uri, Protocol.Ice);

            try
            {
                CheckIcePath(path);
                CheckFragment(fragment);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException(
                    $"Cannot create an ice service address from URI '{uri}'.",
                    nameof(uri),
                    exception);
            }
            _path = path;
            _fragment = fragment;
            _serverAddress = serverAddress;
            _altServerAddresses = altServerAddresses;

            if (serverAddress is null)
            {
                // Without an authority, the query holds service address parameters; adapter-id is the only one.
                foreach ((string name, string value) in queryParams)
                {
                    if (name != "adapter-id" || value.Length == 0)
                    {
                        throw new ArgumentException(
                            $"Invalid service address parameter '{name}' in URI '{uri}'.",
                            nameof(uri));
                    }
                    _adapterId = Uri.UnescapeDataString(value);
                }
            }
        }

        /// <summary>Determines whether the specified service address is equal to this service address.</summary>
        /// <param name="other">The service address to compare with this service address.</param>
        /// <returns><see langword="true" /> if the two service addresses are equal; otherwise, <see langword="false" />.
        /// </returns>
        public bool Equals(Ice? other) =>
            other is not null &&
            (ReferenceEquals(this, other) ||
                (Path == other.Path &&
                    Fragment == other.Fragment &&
                    AdapterId == other.AdapterId &&
                    ServerAddress == other.ServerAddress &&
                    AltServerAddresses.SequenceEqual(other.AltServerAddresses)));

        /// <summary>Serves as the default hash function.</summary>
        /// <returns>A hash code for this service address.</returns>
        public override int GetHashCode() =>
            HashCode.Combine(Path, Fragment, AdapterId, _serverAddress, _altServerAddresses.Count);

        /// <summary>Converts this service address into a string.</summary>
        /// <returns>The URI of this service address.</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            bool firstOption = AppendServerAddresses(sb, Protocol.Ice, Path, _serverAddress, _altServerAddresses);

            if (AdapterId.Length > 0)
            {
                sb.Append(firstOption ? '?' : '&');
                sb.Append("adapter-id=");
                sb.Append(EscapeAdapterId(AdapterId));
            }

            if (Fragment.Length > 0)
            {
                sb.Append('#');
                sb.Append(Fragment);
            }
            return sb.ToString();
        }

        /// <summary>Converts this service address into a URI.</summary>
        /// <returns>The URI of this service address.</returns>
        public Uri ToUri() => new(ToString(), UriKind.Absolute);

        /// <summary>Constructs an ice service address without validation.</summary>
        internal Ice(
            string path,
            ServerAddress? serverAddress,
            ImmutableList<ServerAddress> altServerAddresses,
            string adapterId,
            string fragment)
        {
            _path = path;
            _serverAddress = serverAddress;
            _altServerAddresses = altServerAddresses;
            _adapterId = adapterId;
            _fragment = fragment;
        }

        /// <summary>Checks if a path is a valid URI absolute path with at most two segments, the category and the
        /// name of an Ice identity.</summary>
        /// <remarks>The default path <c>/</c> is valid: sending a request to the null identity is in itself ok. With
        /// an Ice server, it results in a dispatch exception with status code <see cref="StatusCode.NotFound" />.
        /// </remarks>
        private static void CheckIcePath(string path)
        {
            CheckPath(path);
            int firstSlash = path.IndexOf('/', 1, StringComparison.Ordinal);
            if (firstSlash != -1 && firstSlash != path.LastIndexOf('/', StringComparison.Ordinal))
            {
                throw new FormatException($"Too many slashes in path '{path}'.");
            }
        }

        /// <summary>Percent-encodes only the characters that are not valid in a URI query parameter value:
        /// characters outside the printable ASCII range <c>\x21..\x7E</c>, the characters that are invalid in a
        /// parameter value, and the <c>%</c> character itself, which must be escaped to make the result
        /// unambiguously decodable.</summary>
        /// <remarks>This is intentionally narrower than <see cref="Uri.EscapeDataString(string)" />, which
        /// over-escapes characters that are valid in a parameter value such as <c>/</c>, <c>:</c> and <c>@</c>.
        /// </remarks>
        private static string EscapeAdapterId(string value)
        {
            ReadOnlySpan<char> span = value.AsSpan();

            // Adapter IDs are usually pure ASCII so we almost always take this path.
            if (span.IndexOfAnyExceptInRange(FirstValidChar, LastValidChar) == -1 &&
                span.IndexOfAny(_mustEscapeInAdapterId) == -1)
            {
                return value;
            }

            // Encode the whole string to UTF-8 bytes, then percent-escape every byte that is not a valid unescaped
            // char. UTF-8 continuation bytes (>= 0x80) fall in the escape branch, so multi-byte code points need no
            // surrogate-pair logic here.
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            var sb = new StringBuilder(utf8.Length + 8);
            foreach (byte b in utf8)
            {
                if (b >= FirstValidChar && b <= LastValidChar && !_mustEscapeInAdapterId.Contains((char)b))
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>The address of a service reachable with the icerpc protocol.</summary>
    // The properties of this class are sorted in URI order.
    public sealed record class IceRpc
    {
        /// <summary>Gets or initializes the main server address of this service address.</summary>
        /// <value>The main server address, an icerpc server address, or <see langword="null" /> if this service
        /// address has no server address.</value>
        public ServerAddress? ServerAddress
        {
            get => _serverAddress;

            init
            {
                if (value is ServerAddress serverAddress)
                {
                    if (serverAddress.Protocol != Protocol.IceRpc)
                    {
                        throw new ArgumentException(
                            $"The {nameof(ServerAddress)} of an icerpc service address must be an icerpc server address.",
                            nameof(value));
                    }
                }
                else if (_altServerAddresses.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot clear {nameof(ServerAddress)} when {nameof(AltServerAddresses)} is not empty.");
                }
                _serverAddress = value;
            }
        }

        /// <summary>Gets or initializes the path of this service address.</summary>
        /// <value>The path of this service address. Defaults to <c>/</c>.</value>
        public string Path
        {
            get => _path;

            init
            {
                try
                {
                    CheckPath(value);
                }
                catch (FormatException exception)
                {
                    throw new ArgumentException("Invalid path.", nameof(value), exception);
                }
                _path = value;
            }
        }

        /// <summary>Gets or initializes the secondary server addresses of this service address.</summary>
        /// <value>The secondary server addresses of this service address, all icerpc server addresses. Defaults to
        /// <see cref="ImmutableList{T}.Empty" />.</value>
        public ImmutableList<ServerAddress> AltServerAddresses
        {
            get => _altServerAddresses;
            init => _altServerAddresses = CheckAltServerAddresses(value, _serverAddress, Protocol.IceRpc);
        }

        private ImmutableList<ServerAddress> _altServerAddresses = ImmutableList<ServerAddress>.Empty;
        private string _path = "/";
        private ServerAddress? _serverAddress;

        /// <summary>Constructs an icerpc service address with default values.</summary>
        public IceRpc()
        {
        }

        /// <summary>Constructs an icerpc service address from a URI.</summary>
        /// <param name="uri">An absolute URI with the <c>icerpc</c> scheme, such as
        /// <c>icerpc://host:port/path?transport=quic&#38;alt-server=host2</c> or <c>icerpc:/path</c>.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="uri" /> is not a valid icerpc service
        /// address URI.</exception>
        public IceRpc(Uri uri)
        {
            (string path, ServerAddress? serverAddress, ImmutableList<ServerAddress> altServerAddresses,
                ImmutableDictionary<string, string> queryParams, string fragment) = ParseUri(uri, Protocol.IceRpc);

            if (fragment.Length > 0)
            {
                throw new ArgumentException(
                    $"Cannot create an icerpc service address with a fragment from URI '{uri}'.",
                    nameof(uri));
            }

            if (queryParams.Count > 0)
            {
                throw new ArgumentException(
                    $"Cannot create an icerpc service address with a parameter other than transport and alt-server from URI '{uri}'.",
                    nameof(uri));
            }

            _path = path;
            _serverAddress = serverAddress;
            _altServerAddresses = altServerAddresses;
        }

        /// <summary>Determines whether the specified service address is equal to this service address.</summary>
        /// <param name="other">The service address to compare with this service address.</param>
        /// <returns><see langword="true" /> if the two service addresses are equal; otherwise, <see langword="false" />.
        /// </returns>
        public bool Equals(IceRpc? other) =>
            other is not null &&
            (ReferenceEquals(this, other) ||
                (Path == other.Path &&
                    ServerAddress == other.ServerAddress &&
                    AltServerAddresses.SequenceEqual(other.AltServerAddresses)));

        /// <summary>Serves as the default hash function.</summary>
        /// <returns>A hash code for this service address.</returns>
        public override int GetHashCode() => HashCode.Combine(Path, _serverAddress, _altServerAddresses.Count);

        /// <summary>Converts this service address into a string.</summary>
        /// <returns>The URI of this service address.</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            _ = AppendServerAddresses(sb, Protocol.IceRpc, Path, _serverAddress, _altServerAddresses);
            return sb.ToString();
        }

        /// <summary>Converts this service address into a URI.</summary>
        /// <returns>The URI of this service address.</returns>
        public Uri ToUri() => new(ToString(), UriKind.Absolute);

        /// <summary>Constructs an icerpc service address without validation.</summary>
        internal IceRpc(string path, ServerAddress? serverAddress, ImmutableList<ServerAddress> altServerAddresses)
        {
            _path = path;
            _serverAddress = serverAddress;
            _altServerAddresses = altServerAddresses;
        }
    }

    /// <summary>Gets the protocol of this service address.</summary>
    /// <value><see cref="Protocol.Ice" /> for an <see cref="Ice" /> service address and
    /// <see cref="Protocol.IceRpc" /> for an <see cref="IceRpc" /> service address.</value>
    public Protocol Protocol => this switch
    {
        IceRpc => Protocol.IceRpc,
        Ice => Protocol.Ice,
    };

    /// <summary>Gets the main server address of this service address.</summary>
    /// <value>The main server address of the variant, or <see langword="null" /> if the variant has no server
    /// address.</value>
    public ServerAddress? ServerAddress => this switch
    {
        IceRpc icerpc => icerpc.ServerAddress,
        Ice ice => ice.ServerAddress,
    };

    /// <summary>Gets the path of this service address.</summary>
    /// <value>The path of the variant.</value>
    public string Path => this switch
    {
        IceRpc icerpc => icerpc.Path,
        Ice ice => ice.Path,
    };

    /// <summary>Gets the secondary server addresses of this service address.</summary>
    /// <value>The secondary server addresses of the variant.</value>
    public ImmutableList<ServerAddress> AltServerAddresses => this switch
    {
        IceRpc icerpc => icerpc.AltServerAddresses,
        Ice ice => ice.AltServerAddresses,
    };

    // The printable ASCII character range is x20 (space) to x7E inclusive. Space is an invalid character in path,
    // fragment, etc. in addition to the invalid characters in the _notValidInXXX search values.
    private const char FirstValidChar = '\x21';
    private const char LastValidChar = '\x7E';

    // The characters that are not valid in a query parameter value, plus '%'.
    private static readonly SearchValues<char> _mustEscapeInAdapterId = SearchValues.Create("\"<>#%&\\^`{|}");
    private static readonly SearchValues<char> _notValidInFragment = SearchValues.Create("\"<>\\^`{|}");
    private static readonly SearchValues<char> _notValidInPath = SearchValues.Create("\"<>#?\\^`{|}");

    /// <summary>Creates a service address from a URI.</summary>
    /// <param name="uri">An absolute URI whose scheme is a supported protocol.</param>
    /// <returns>An <see cref="Ice" /> or <see cref="IceRpc" /> service address, depending on the scheme of
    /// <paramref name="uri" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="uri" /> is not a valid service address URI.
    /// </exception>
    public static ServiceAddress FromUri(Uri uri) =>
        uri.IsAbsoluteUri && Protocol.TryParse(uri.Scheme, out Protocol? protocol) ?
            (protocol == Protocol.Ice ? new Ice(uri) : new IceRpc(uri)) :
            throw new ArgumentException($"Cannot create a service address from URI '{uri}'.", nameof(uri));

    /// <summary>Determines whether the specified service address is equal to this service address.</summary>
    /// <param name="other">The service address to compare with this service address.</param>
    /// <returns><see langword="true" /> if the two service addresses hold equal variants, or both hold no variant;
    /// otherwise, <see langword="false" />.</returns>
    public bool Equals(ServiceAddress other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ServiceAddress other && Equals(other);

    /// <summary>Serves as the default hash function.</summary>
    /// <returns>A hash code for this service address.</returns>
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Converts this service address into a string.</summary>
    /// <returns>The URI of the variant, or an empty string when this service address holds no variant.</returns>
    public override string ToString() => Value?.ToString() ?? "";

    /// <summary>Converts this service address into a URI.</summary>
    /// <returns>The URI of the variant.</returns>
    public Uri ToUri() => new(ToString(), UriKind.Absolute);

    /// <summary>Determines whether two service addresses are equal.</summary>
    /// <param name="left">The first service address.</param>
    /// <param name="right">The second service address.</param>
    /// <returns><see langword="true" /> if the service addresses are equal; otherwise, <see langword="false" />.
    /// </returns>
    public static bool operator ==(ServiceAddress left, ServiceAddress right) => left.Equals(right);

    /// <summary>Determines whether two service addresses are not equal.</summary>
    /// <param name="left">The first service address.</param>
    /// <param name="right">The second service address.</param>
    /// <returns><see langword="true" /> if the service addresses are not equal; otherwise, <see langword="false" />.
    /// </returns>
    public static bool operator !=(ServiceAddress left, ServiceAddress right) => !left.Equals(right);

    /// <summary>Checks if <paramref name="path" /> is a properly escaped URI absolute path, i.e. that it starts
    /// with a <c>/</c> and contains only unreserved characters, <c>%</c>, and reserved characters other than
    /// <c>?</c> and <c>#</c>.</summary>
    /// <param name="path">The path to check.</param>
    /// <exception cref="FormatException">Thrown when the path is not valid.</exception>
    /// <remarks>The absolute path of a URI with a supported protocol satisfies these requirements.</remarks>
    internal static void CheckPath(string path)
    {
        if (path.Length == 0 || path[0] != '/' || !IsValid(path, _notValidInPath))
        {
            throw new FormatException(
                $"Invalid path '{path}'; a valid path starts with '/' and contains only unreserved characters, '%', and reserved characters other than '?' and '#'.");
        }
    }

    /// <summary>Appends the URI of a service address, up to and including its alt-server parameter.</summary>
    /// <returns><see langword="true" /> when the appended string has no query; otherwise, <see langword="false" />.
    /// </returns>
    private static bool AppendServerAddresses(
        StringBuilder sb,
        Protocol protocol,
        string path,
        ServerAddress? serverAddress,
        ImmutableList<ServerAddress> altServerAddresses)
    {
        bool firstOption = true;

        if (serverAddress is ServerAddress mainServerAddress)
        {
            sb.AppendServerAddress(mainServerAddress, path);
            firstOption = mainServerAddress.Params.Count == 0 && mainServerAddress.Transport is null;
        }
        else
        {
            sb.Append(protocol);
            sb.Append(':');
            sb.Append(path);
        }

        if (altServerAddresses.Count > 0)
        {
            sb.Append(firstOption ? '?' : '&');
            firstOption = false;
            sb.Append("alt-server=");
            for (int i = 0; i < altServerAddresses.Count; ++i)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.AppendServerAddress(altServerAddresses[i], path: "", includeScheme: false, paramSeparator: '$');
            }
        }
        return firstOption;
    }

    /// <summary>Checks that the alt server addresses use the protocol of the service address and that the service
    /// address has a main server address when the list is not empty.</summary>
    private static ImmutableList<ServerAddress> CheckAltServerAddresses(
        ImmutableList<ServerAddress> altServerAddresses,
        ServerAddress? serverAddress,
        Protocol protocol)
    {
        if (altServerAddresses.Count > 0)
        {
            if (serverAddress is null)
            {
                throw new InvalidOperationException(
                    $"Cannot set {nameof(AltServerAddresses)} when {nameof(ServerAddress)} is empty.");
            }

            if (altServerAddresses.Any(e => e.Protocol != protocol))
            {
                throw new ArgumentException(
                    $"The {nameof(AltServerAddresses)} server addresses must be {protocol} server addresses.",
                    nameof(altServerAddresses));
            }
        }
        return altServerAddresses;
    }

    /// <summary>Checks if <paramref name="fragment" /> is a properly escaped URI fragment, i.e. it contains only
    /// unreserved characters, reserved characters, and '%'.</summary>
    /// <remarks>The fragment of a URI with a supported protocol satisfies these requirements.</remarks>
    private static void CheckFragment(string fragment)
    {
        if (!IsValid(fragment, _notValidInFragment))
        {
            throw new FormatException(
                $"Invalid fragment '{fragment}'; a valid fragment contains only unreserved characters, reserved characters, and '%'.");
        }
    }

    private static bool IsValid(string s, SearchValues<char> invalidChars)
    {
        ReadOnlySpan<char> span = s.AsSpan();
        return span.IndexOfAnyExceptInRange(FirstValidChar, LastValidChar) == -1 && span.IndexOfAny(invalidChars) == -1;
    }

    /// <summary>Parses a service address URI into its components.</summary>
    /// <returns>The path, the server addresses, the query parameters other than alt-server and transport, and the
    /// fragment without its leading <c>#</c>. With an authority, an ice main server address also carries the query
    /// parameters.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="uri" /> is not an absolute URI with the scheme
    /// of <paramref name="protocol" />, or when its authority or query is not valid for a service address.</exception>
    private static (string Path, ServerAddress? ServerAddress, ImmutableList<ServerAddress> AltServerAddresses, ImmutableDictionary<string, string> QueryParams, string Fragment) ParseUri(
        Uri uri,
        Protocol protocol)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != protocol.Name)
        {
            throw new ArgumentException($"Cannot create an {protocol} service address from URI '{uri}'.", nameof(uri));
        }

        // The AbsolutePath is empty for a URI such as "icerpc:?foo=bar"
        string path = uri.AbsolutePath.Length > 0 ? uri.AbsolutePath : "/";
        string fragment = uri.Fragment.Length > 0 ? uri.Fragment[1..] : "";

        (ImmutableDictionary<string, string> queryParams, string? altServerValue, string? transport) =
            uri.ParseQuery();

        ServerAddress? serverAddress = null;
        ImmutableList<ServerAddress> altServerAddresses = ImmutableList<ServerAddress>.Empty;

        if (uri.Authority.Length > 0)
        {
            if (uri.UserInfo.Length > 0)
            {
                throw new ArgumentException("Cannot create a server address with a user info.", nameof(uri));
            }

            string host = uri.IdnHost;
            Debug.Assert(host.Length > 0); // the IdnHost provided by Uri is never empty
            ushort port = uri.Port == -1 ? protocol.DefaultPort : checked((ushort)uri.Port);

            serverAddress = new ServerAddress(protocol, host, port, transport, queryParams);

            if (altServerValue is not null)
            {
                // Split and parse recursively each server address
                foreach (string serverAddressStr in altServerValue.Split(','))
                {
                    // The separator for server address parameters in alt-server is $, so we replace these '$' by '&'
                    // before sending the string (Uri) to the server address constructor which uses '&' as separator.
                    var altUri = new Uri($"{uri.Scheme}://{serverAddressStr}".Replace('$', '&'));
                    altServerAddresses = altServerAddresses.Add(new ServerAddress(altUri));
                }
            }
        }
        else
        {
            if (!path.StartsWith('/', StringComparison.Ordinal))
            {
                throw new ArgumentException($"Invalid path in service address URI '{uri}'.", nameof(uri));
            }

            if (altServerValue is not null)
            {
                throw new ArgumentException($"Invalid alt-server parameter in URI '{uri}'.", nameof(uri));
            }
        }

        return (path, serverAddress, altServerAddresses, queryParams, fragment);
    }
}

/// <summary>The service address type converter specifies how to convert a string to a service address. It's used by
/// sub-systems such as the Microsoft ConfigurationBinder to bind string values to ServiceAddress properties.</summary>
public class ServiceAddressTypeConverter : TypeConverter
{
    /// <summary>Returns whether this converter can convert an object of the given type into a
    /// <see cref="ServiceAddress"/> object, using the specified context.</summary>
    /// <param name="context">An <see cref="ITypeDescriptorContext"/> that provides a format context.</param>
    /// <param name="sourceType">A <see cref="Type"/> that represents the type you want to convert from.</param>
    /// <returns><see langword="true"/>if this converter can perform the conversion; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <summary>Converts the given object into a <see cref="ServiceAddress"/> object, using the specified context and culture
    /// information.</summary>
    /// <param name="context">An <see cref="ITypeDescriptorContext"/> that provides a format context.</param>
    /// <param name="culture">The <see cref="CultureInfo"/> to use as the current culture.</param>
    /// <param name="value">The <see cref="object "/> to convert.</param>
    /// <returns>An <see cref="object "/> that represents the converted <see cref="ServiceAddress"/>.</returns>
    /// <remarks><see cref="TypeConverter"/>.</remarks>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string valueStr ?
            ServiceAddress.FromUri(new Uri(valueStr)) : base.ConvertFrom(context, culture, value);
}
