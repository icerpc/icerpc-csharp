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
/// construct an <see cref="OutgoingRequest" />.</summary>
// The properties of this class are sorted in URI order.
[TypeConverter(typeof(ServiceAddressTypeConverter))]
public sealed record class ServiceAddress
{
    /// <summary>Gets the protocol of this service address.</summary>
    /// <value>The protocol of the service address. It corresponds to the URI scheme and is <see langword="null" /> for
    /// a relative service address.</value>
    public Protocol? Protocol { get; }

    /// <summary>Gets or initializes the main server address of this service address.</summary>
    /// <value>The main server address of this service address, or <see langword="null"/> if this service address has no
    /// server address.</value>
    public ServerAddress? ServerAddress
    {
        get => _serverAddress;

        init
        {
            if (Protocol is null)
            {
                throw new InvalidOperationException(
                    $"Cannot set {nameof(ServerAddress)} on a relative service address.");
            }

            if (value?.Protocol is Protocol newProtocol && newProtocol != Protocol)
            {
                throw new ArgumentException(
                    $"The {nameof(ServerAddress)} must use the service address's protocol: '{Protocol}'.",
                    nameof(value));
            }

            if (value is not null)
            {
                if (_params.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot set {nameof(ServerAddress)} on a service address with parameters.");
                }
            }
            else if (_altServerAddresses.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot clear {nameof(ServerAddress)} when {nameof(AltServerAddresses)} is not empty.");
            }
            _serverAddress = value;
            OriginalUri = null;
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
                CheckPath(value); // make sure it's properly escaped
                Protocol?.CheckPath(value); // make sure the protocol is happy with this path
            }
            catch (FormatException exception)
            {
                throw new ArgumentException("Invalid path.", nameof(value), exception);
            }
            _path = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets or initializes the secondary server addresses of this service address.</summary>
    /// <value>The secondary server addresses of this service address. Defaults to <see cref="ImmutableList{T}.Empty"
    /// />.</value>
    public ImmutableList<ServerAddress> AltServerAddresses
    {
        get => _altServerAddresses;

        init
        {
            if (Protocol is null)
            {
                throw new InvalidOperationException(
                    $"Cannot set {nameof(AltServerAddresses)} on a relative service address.");
            }

            if (value.Count > 0)
            {
                if (_serverAddress is null)
                {
                    throw new InvalidOperationException(
                        $"Cannot set {nameof(AltServerAddresses)} when {nameof(ServerAddress)} is empty.");
                }

                if (value.Any(e => e.Protocol != Protocol))
                {
                    throw new ArgumentException(
                        $"The {nameof(AltServerAddresses)} server addresses must use the service address's protocol: '{Protocol}'.",
                        nameof(value));
                }
            }
            // else, no need to check anything, an empty list is always fine.

            _altServerAddresses = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets or initializes the parameters of this service address.</summary>
    /// <value>The params dictionary. Always empty if <see cref="ServerAddress" /> is not <see langword="null"/>.
    /// Defaults to <see cref="ImmutableDictionary{TKey, TValue}.Empty" />.</value>.
    public ImmutableDictionary<string, string> Params
    {
        get => _params;
        init
        {
            if (Protocol is null)
            {
                throw new InvalidOperationException($"Cannot set {nameof(Params)} on a relative service address.");
            }

            try
            {
                CheckParams(value); // general checking (properly escape, no empty name)
                Protocol.CheckServiceAddressParams(value); // protocol-specific checking
            }
            catch (FormatException exception)
            {
                throw new ArgumentException("Invalid parameters.", nameof(value), exception);
            }

            if (_serverAddress is not null && value.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot set {nameof(Params)} on a service address with a serverAddress.");
            }

            _params = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets or initializes the fragment.</summary>
    /// <value>The fragment of this service address. Defaults to an empty string.</value>
    public string Fragment
    {
        get => _fragment;
        init
        {
            if (Protocol is null)
            {
                throw new InvalidOperationException($"Cannot set {nameof(Fragment)} on a relative service address.");
            }

            try
            {
                CheckFragment(value); // make sure it's properly escaped
            }
            catch (FormatException exception)
            {
                throw new ArgumentException("Invalid fragment.", nameof(value), exception);
            }

            if (!Protocol.HasFragment && value.Length > 0)
            {
                throw new InvalidOperationException($"Cannot set {Fragment} on an {Protocol} service address.");
            }

            _fragment = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets the URI used to create this service address.</summary>
    /// <value>The <see cref="Uri" /> of this service address if it was constructed from a URI and if URI-derived
    /// properties have not been updated. The setting of a URI-derived property such as <see cref="ServerAddress" />
    /// sets <see cref="OriginalUri" /> to <see langword="null"/>.</value>
    public Uri? OriginalUri { get; private set; }

    // The printable ASCII character range is x20 (space) to x7E inclusive. Space is an invalid character in path,
    // fragment, etc. in addition to the invalid characters in the _notValidInXXX search values.
    private const char FirstValidChar = '\x21';
    private const char LastValidChar = '\x7E';

    private static readonly SearchValues<char> _notValidInFragment = SearchValues.Create("\"<>\\^`{|}");
    private static readonly SearchValues<char> _notValidInParamName = SearchValues.Create("\"<>#&=\\^`{|}");
    private static readonly SearchValues<char> _notValidInParamValue = SearchValues.Create("\"<>#&\\^`{|}");
    private static readonly SearchValues<char> _notValidInPath = SearchValues.Create("\"<>#?\\^`{|}");

    private ImmutableList<ServerAddress> _altServerAddresses = ImmutableList<ServerAddress>.Empty;
    private string _fragment = "";
    private ImmutableDictionary<string, string> _params = ImmutableDictionary<string, string>.Empty;
    private string _path = "/";
    private ServerAddress? _serverAddress;

    /// <summary>Constructs a service address from a protocol.</summary>
    /// <param name="protocol">The protocol, or <see langword="null" /> for a relative service address.</param>
    public ServiceAddress(Protocol? protocol = null) => Protocol = protocol;

    /// <summary>Constructs a service address from a URI.</summary>
    /// <param name="uri">The Uri.</param>
    public ServiceAddress(Uri uri)
    {
        if (uri.IsAbsoluteUri)
        {
            Protocol = Protocol.TryParse(uri.Scheme, out Protocol? protocol) ? protocol :
                throw new ArgumentException(
                    $"Cannot create a service address with protocol '{uri.Scheme}'.",
                    nameof(uri));

            // The AbsolutePath is empty for a URI such as "icerpc:?foo=bar"
            _path = uri.AbsolutePath.Length > 0 ? uri.AbsolutePath : "/";
            _fragment = uri.Fragment.Length > 0 ? uri.Fragment[1..] : ""; // remove leading #

            try
            {
                Protocol.CheckPath(_path);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException($"Invalid path in {Protocol} URI.", nameof(uri), exception);
            }

            if (!Protocol.HasFragment && _fragment.Length > 0)
            {
                throw new ArgumentException(
                    $"Cannot create an {Protocol} service address with a fragment.",
                    nameof(uri));
            }

            (ImmutableDictionary<string, string> queryParams, string? altServerValue, string? transport) =
                uri.ParseQuery();

            if (uri.Authority.Length > 0)
            {
                if (uri.UserInfo.Length > 0)
                {
                    throw new ArgumentException("Cannot create a server address with a user info.", nameof(uri));
                }

                string host = uri.IdnHost;
                Debug.Assert(host.Length > 0); // the IdnHost provided by Uri is never empty

                _serverAddress = new ServerAddress(
                    Protocol,
                    host,
                    port: uri.Port == -1 ? Protocol.DefaultPort : checked((ushort)uri.Port),
                    transport,
                    queryParams);

                if (altServerValue is not null)
                {
                    // Split and parse recursively each serverAddress
                    foreach (string serverAddressStr in altServerValue.Split(','))
                    {
                        string altUriString = $"{uri.Scheme}://{serverAddressStr}";

                        // The separator for server address parameters in alt-server is $, so we replace these '$'
                        // by '&' before sending the string (Uri) to the ServerAddress constructor which uses '&' as
                        // separator.
                        _altServerAddresses = _altServerAddresses.Add(
                            new ServerAddress(new Uri(altUriString.Replace('$', '&'))));
                    }
                }
            }
            else
            {
                if (!_path.StartsWith('/'))
                {
                    throw new ArgumentException(
                        $"Invalid path in service address URI '{uri.OriginalString}'.",
                        nameof(uri));
                }

                if (altServerValue is not null)
                {
                    throw new ArgumentException(
                        $"Invalid alt-server parameter in URI '{uri.OriginalString}'.",
                        nameof(uri));
                }

                try
                {
                    Protocol.CheckServiceAddressParams(queryParams);
                }
                catch (FormatException exception)
                {
                    throw new ArgumentException("Invalid parameters in URI.", nameof(uri), exception);
                }

                Params = queryParams;
            }
        }
        else
        {
            // relative service address
            Protocol = null;
            _path = uri.ToString();

            try
            {
                CheckPath(_path);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException("Invalid path in relative URI.", nameof(uri), exception);
            }
        }

        OriginalUri = uri;
    }

    /// <summary>Determines whether the specified <see cref="ServiceAddress"/> is equal to the current
    /// <see cref="ServiceAddress"/>.</summary>
    /// <param name="other">The <see cref="ServiceAddress"/> to compare with the current <see cref="ServiceAddress"/>.
    /// </param>
    /// <returns><see langword="true"/> if the specified <see cref="ServiceAddress"/> is equal to the current
    /// <see cref="ServiceAddress"/>; otherwise, <see langword="false"/>.</returns>
    public bool Equals(ServiceAddress? other)
    {
        if (other is null)
        {
            return false;
        }
        else if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Protocol != other.Protocol)
        {
            return false;
        }

        if (Protocol is null)
        {
            // Both service addresses are relative
            return Path == other.Path;
        }

        // Comparing 2 service addresses with the same protocol
        return Path == other.Path &&
            Fragment == other.Fragment &&
            ServerAddress == other.ServerAddress &&
            AltServerAddresses.SequenceEqual(other.AltServerAddresses) &&
            Params.DictionaryEqual(other.Params);
    }

    /// <summary>Serves as the default hash function.</summary>
    /// <returns>A hash code for the current <see cref="ServiceAddress"/>.</returns>
    public override int GetHashCode()
    {
        if (Protocol is null)
        {
            return Path.GetHashCode(StringComparison.Ordinal);
        }

        // We only hash a subset of the properties to keep GetHashCode reasonably fast.
        var hash = new HashCode();
        hash.Add(Protocol);
        hash.Add(Path);
        hash.Add(Fragment);
        hash.Add(_serverAddress);
        hash.Add(_altServerAddresses.Count);
        return hash.ToHashCode();
    }

    /// <summary>Converts this service address into a string.</summary>
    /// <returns>The string representation of this service address.</returns>
    public override string ToString()
    {
        if (Protocol is null)
        {
            return Path;
        }
        else if (OriginalUri is Uri uri)
        {
            return uri.ToString();
        }

        // else, construct a string with a string builder.

        var sb = new StringBuilder();
        bool firstOption = true;

        if (ServerAddress is ServerAddress serverAddress)
        {
            sb.AppendServerAddress(serverAddress, Path);
            firstOption = serverAddress.Params.Count == 0;
        }
        else
        {
            sb.Append(Protocol);
            sb.Append(':');
            sb.Append(Path);
        }

        if (AltServerAddresses.Count > 0)
        {
            StartQueryOption(sb, ref firstOption);
            sb.Append("alt-server=");
            for (int i = 0; i < AltServerAddresses.Count; ++i)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.AppendServerAddress(AltServerAddresses[i], path: "", includeScheme: false, paramSeparator: '$');
            }
        }

        foreach ((string name, string value) in Params)
        {
            StartQueryOption(sb, ref firstOption);
            sb.Append(name);
            if (value.Length > 0)
            {
                sb.Append('=');
                sb.Append(value);
            }
        }

        if (Fragment.Length > 0)
        {
            sb.Append('#');
            sb.Append(Fragment);
        }

        return sb.ToString();

        static void StartQueryOption(StringBuilder sb, ref bool firstOption)
        {
            if (firstOption)
            {
                sb.Append('?');
                firstOption = false;
            }
            else
            {
                sb.Append('&');
            }
        }
    }

    /// <summary>Converts this service address into a Uri.</summary>
    /// <returns>An Uri representing this service address.</returns>
    public Uri ToUri() =>
        OriginalUri ?? (Protocol is null ? new Uri(Path, UriKind.Relative) : new Uri(ToString(), UriKind.Absolute));

    /// <summary>Checks if <paramref name="params" /> contains properly escaped names and values.</summary>
    /// <param name="params">The dictionary to check.</param>
    /// <exception cref="FormatException">Thrown if the dictionary is not valid.</exception>
    /// <remarks>A dictionary returned by <see cref="UriExtensions.ParseQuery" /> is properly escaped.</remarks>
    internal static void CheckParams(ImmutableDictionary<string, string> @params)
    {
        foreach ((string name, string value) in @params)
        {
            if (!IsValidParamName(name))
            {
                throw new FormatException($"Invalid parameter name '{name}'.");
            }
            if (!IsValidParamValue(value))
            {
                throw new FormatException($"Invalid parameter value '{value}'.");
            }
        }
    }

    /// <summary>Checks if <paramref name="path" /> is a properly escaped URI absolute path, i.e. that it starts
    /// with a <c>/</c> and contains only unreserved characters, <c>%</c>, and reserved characters other than
    /// <c>?</c> and <c>#</c>.</summary>
    /// <param name="path">The path to check.</param>
    /// <exception cref="FormatException">Thrown if the path is not valid.</exception>
    /// <remarks>The absolute path of a URI with a supported protocol satisfies these requirements.</remarks>
    internal static void CheckPath(string path)
    {
        if (path.Length == 0 || path[0] != '/' || !IsValid(path, _notValidInPath))
        {
            throw new FormatException(
                $"Invalid path '{path}'; a valid path starts with '/' and contains only unreserved characters, '%', and reserved characters other than '?' and '#'.");
        }
    }

    /// <summary>Checks if <paramref name="value" /> contains only unreserved characters, <c>%</c>, and reserved
    /// characters other than <c>#</c> and <c>&#38;</c>.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><see langword="true" /> if <paramref name="value" /> is a valid parameter value; otherwise,
    /// <see langword="false" />.</returns>
    internal static bool IsValidParamValue(string value) => IsValid(value, _notValidInParamValue);

    /// <summary>"unchecked" constructor used by the Slice decoder when decoding a Slice1 encoded service address.
    /// </summary>
    internal ServiceAddress(
        Protocol protocol,
        string path,
        ServerAddress? serverAddress,
        ImmutableList<ServerAddress> altServerAddresses,
        ImmutableDictionary<string, string> serviceAddressParams,
        string fragment)
    {
        Protocol = protocol;
        _path = path;
        _serverAddress = serverAddress;
        _altServerAddresses = altServerAddresses;
        _params = serviceAddressParams;
        _fragment = fragment;
    }

    /// <summary>Checks if <paramref name="fragment" /> is a properly escaped URI fragment, i.e. it contains only
    /// unreserved characters, reserved characters, and '%'.</summary>
    /// <param name="fragment">The fragment to check.</param>
    /// <exception cref="FormatException">Thrown if the fragment is not valid.</exception>
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

    /// <summary>Checks if <paramref name="name" /> is not empty, not equal to <c>alt-server</c> nor equal to
    /// <c>transport</c> and contains only unreserved characters, <c>%</c>, or reserved characters other than <c>#</c>,
    /// <c>&#38;</c> and <c>=</c>.</summary>
    /// <param name="name">The name to check.</param>
    /// <returns><see langword="true" /> if <paramref name="name" /> is a valid parameter name; otherwise,
    /// <see langword="false" />.</returns>
    /// <remarks>The range of valid names is much larger than the range of names you should use. For example, you
    /// should avoid parameter names with a <c>%</c> or <c>$</c> character, even though these characters are valid
    /// in a name.</remarks>
    private static bool IsValidParamName(string name) =>
        name.Length > 0 && name != "alt-server" && name != "transport" && IsValid(name, _notValidInParamName);
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
        value is string valueStr ? new ServiceAddress(new Uri(valueStr)) : base.ConvertFrom(context, culture, value);
}
