// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace IceRpc;

/// <summary>A service address corresponds to the URI of a service, parsed and processed for easier consumption by
/// interceptors, <see cref="ConnectionCache"/> and other elements of the invocation pipeline. It's used to construct
/// an <see cref="OutgoingRequest"/>.</summary>
// The properties of this class are sorted in URI order.
[TypeConverter(typeof(ServiceAddressTypeConverter))]
public sealed record class ServiceAddress
{
    /// <summary>Gets the protocol of this service address.</summary>
    /// <value>The protocol of the service address. It corresponds to the URI scheme and is null for a relative service
    /// address.</value>
    public Protocol? Protocol { get; }

    /// <summary>Gets or initializes the main server address of this service address.</summary>
    /// <value>The main server address of this service address, or null if this service address has no server address.
    /// </value>
    public ServerAddress? ServerAddress
    {
        get => _serverAddress;

        init
        {
            CheckSupportedProtocol(nameof(ServerAddress));
            if (value?.Protocol is Protocol newProtocol && newProtocol != Protocol)
            {
                throw new ArgumentException(
                    $"the {nameof(ServerAddress)} must use the service address's protocol: '{Protocol}'",
                    nameof(value));
            }

            if (value is not null)
            {
                if (_params.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"cannot set {nameof(ServerAddress)} on a service address with parameters");
                }
            }
            else if (_altServerAddresses.Count > 0)
            {
                throw new InvalidOperationException(
                    $"cannot clear {nameof(ServerAddress)} when {nameof(AltServerAddresses)} is not empty");
            }
            _serverAddress = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets or initializes the path of this service address.</summary>
    public string Path
    {
        get => _path;
        init
        {
            if (Protocol is null || Protocol.IsSupported)
            {
                try
                {
                    CheckPath(value); // make sure it's properly escaped
                    Protocol?.CheckPath(value); // make sure the protocol is happy with this path
                }
                catch (FormatException ex)
                {
                    throw new ArgumentException("invalid path", nameof(Path), ex);
                }
                _path = value;
                OriginalUri = null;
            }
            else
            {
                throw new InvalidOperationException($"cannot set {nameof(Path)} on a '{Protocol}' service address");
            }
        }
    }

    /// <summary>Gets or initializes the secondary server addresses of this service address.</summary>
    /// <value>The secondary server addresses of this service address.</value>
    public ImmutableList<ServerAddress> AltServerAddresses
    {
        get => _altServerAddresses;

        init
        {
            CheckSupportedProtocol(nameof(AltServerAddresses));

            if (value.Count > 0)
            {
                if (_serverAddress is null)
                {
                    throw new InvalidOperationException(
                        $"cannot set {nameof(AltServerAddresses)} when {nameof(ServerAddress)} is empty");
                }

                if (value.Any(e => e.Protocol != Protocol))
                {
                    throw new ArgumentException(
                        @$"the {nameof(AltServerAddresses)
                        } server addresses must use the service address's protocol {Protocol}",
                        nameof(value));
                }
            }
            // else, no need to check anything, an empty list is always fine.

            _altServerAddresses = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets or initializes the parameters of this service address. Always empty when
    /// <see cref="ServerAddress"/> is not null.</summary>
    public ImmutableDictionary<string, string> Params
    {
        get => _params;
        init
        {
            CheckSupportedProtocol(nameof(Params));

            try
            {
                CheckParams(value); // general checking (properly escape, no empty name)
                Protocol!.CheckServiceAddressParams(value); // protocol-specific checking
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("invalid parameters", nameof(Params), ex);
            }

            if (_serverAddress is not null && value.Count > 0)
            {
                throw new InvalidOperationException(
                    $"cannot set {nameof(Params)} on a service address with an serverAddress");
            }

            _params = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets or initializes the fragment.</summary>
    public string Fragment
    {
        get => _fragment;
        init
        {
            CheckSupportedProtocol(nameof(Fragment));

            try
            {
                CheckFragment(value); // make sure it's properly escaped
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("invalid fragment", nameof(Fragment), ex);
            }

            if (!Protocol!.HasFragment && value.Length > 0)
            {
                throw new InvalidOperationException($"cannot set {Fragment} on an {Protocol} service address");
            }

            _fragment = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets the URI used to create this service address, if this service address was created from a URI and
    /// URI-derived properties such as <see cref="ServerAddress"/> have not been updated.</summary>
    public Uri? OriginalUri { get; private set; }

    private ImmutableList<ServerAddress> _altServerAddresses = ImmutableList<ServerAddress>.Empty;
    private string _fragment = "";
    private ImmutableDictionary<string, string> _params = ImmutableDictionary<string, string>.Empty;
    private string _path = "/";
    private ServerAddress? _serverAddress;

    /// <summary>Constructs a service address from a protocol.</summary>
    /// <param name="protocol">The protocol, or null for a relative service address.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="protocol"/> is not null or a supported protocol.
    /// </exception>
    public ServiceAddress(Protocol? protocol = null) =>
        Protocol = protocol is null || protocol.IsSupported ? protocol :
            throw new ArgumentException("protocol must be null or a supported protocol", nameof(protocol));

    /// <summary>Constructs a service address from a URI.</summary>
    /// <param name="uri">The Uri.</param>
    public ServiceAddress(Uri uri)
    {
        if (uri.IsAbsoluteUri)
        {
            Protocol = Protocol.FromString(uri.Scheme);

            // The AbsolutePath is empty for a URI such as "icerpc:?foo=bar"
            _path = uri.AbsolutePath.Length > 0 ? uri.AbsolutePath : "/";
            _fragment = uri.Fragment.Length > 0 ? uri.Fragment[1..] : ""; // remove leading #

            if (Protocol.IsSupported)
            {
                try
                {
                    Protocol.CheckPath(_path);
                }
                catch (FormatException exception)
                {
                    throw new ArgumentException($"invalid path in {Protocol} URI", nameof(uri), exception);
                }

                if (!Protocol.HasFragment && _fragment.Length > 0)
                {
                    throw new ArgumentException(
                        $"cannot create an {Protocol} service address with a fragment",
                        nameof(uri));
                }

                (ImmutableDictionary<string, string> queryParams, string? altServerValue, string? transport) =
                    uri.ParseQuery();

                if (uri.Authority.Length > 0)
                {
                    if (uri.UserInfo.Length > 0)
                    {
                        throw new ArgumentException("cannot create a server address with a user info", nameof(uri));
                    }

                    string host = uri.IdnHost;
                    Debug.Assert(host.Length > 0); // the IdnHost provided by Uri is never empty

                    _serverAddress = new ServerAddress(
                        Protocol,
                        host,
                        port: checked((ushort)(uri.Port == -1 ? Protocol.DefaultUriPort : uri.Port)),
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
                            $"invalid path in service address URI '{uri.OriginalString}'",
                            nameof(uri));
                    }

                    if (altServerValue is not null)
                    {
                        throw new ArgumentException(
                            $"invalid alt-server parameter in URI '{uri.OriginalString}'",
                            nameof(uri));
                    }

                    try
                    {
                        Protocol.CheckServiceAddressParams(queryParams);
                    }
                    catch (FormatException exception)
                    {
                        throw new ArgumentException("invalid params in URI", nameof(uri), exception);
                    }

                    Params = queryParams;
                }
            }

            // else, not a supported protocol so nothing to do
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
                throw new ArgumentException("invalid path in relative URI", nameof(uri), exception);
            }
        }

        OriginalUri = uri;
    }

    /// <inheritdoc/>
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
        else if (!Protocol.IsSupported)
        {
            // Comparing 2 service addresses with the same non-supported protocol
            Debug.Assert(OriginalUri is not null);
            Debug.Assert(other.OriginalUri is not null);
            return OriginalUri == other.OriginalUri;
        }

        // Comparing 2 service addresses with the same supported protocol
        return Path == other.Path &&
            Fragment == other.Fragment &&
            ServerAddress == other.ServerAddress &&
            AltServerAddresses.SequenceEqual(other.AltServerAddresses) &&
            Params.DictionaryEqual(other.Params);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (Protocol is null)
        {
            return Path.GetHashCode(StringComparison.Ordinal);
        }
        else if (!Protocol.IsSupported)
        {
            Debug.Assert(OriginalUri is not null);
            return OriginalUri.GetHashCode();
        }

        // Service address with a supported protocol

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

    /// <summary>Checks if <paramref name="params"/> contains properly escaped names and values.</summary>
    /// <param name="params">The dictionary to check.</param>
    /// <exception cref="FormatException">Thrown if the dictionary is not valid.</exception>
    /// <remarks>A dictionary returned by <see cref="UriExtensions.ParseQuery"/> is properly escaped.</remarks>
    internal static void CheckParams(ImmutableDictionary<string, string> @params)
    {
        foreach ((string name, string value) in @params)
        {
            if (!IsValidParamName(name))
            {
                throw new FormatException($"invalid parameter name '{name}'");
            }
            if (!IsValidParamValue(value))
            {
                throw new FormatException($"invalid parameter value '{value}'");
            }
        }
    }

    /// <summary>Checks if <paramref name="path"/> is a properly escaped URI absolute path, i.e. that it starts
    /// with a <c>/</c> and contains only unreserved characters, <c>%</c>, or reserved characters other than
    /// <c>?</c> and <c>#</c>.</summary>
    /// <param name="path">The path to check.</param>
    /// <exception cref="FormatException">Thrown if the path is not valid.</exception>
    /// <remarks>The absolute path of a URI with a supported protocol satisfies these requirements.</remarks>
    internal static void CheckPath(string path)
    {
        if (path.Length == 0 || path[0] != '/' || !IsValid(path, "\"<>#?\\^`{|}"))
        {
            throw new FormatException(
                $"invalid path '{path}'; a valid path starts with '/' and contains only unreserved characters, " +
                "'%' or reserved characters other than '?' and '#'");
        }
    }

    /// <summary>Checks if <paramref name="value"/> contains only unreserved characters, <c>%</c>, or reserved
    /// characters other than <c>#</c> and <c>&#38;</c>.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><c>true</c> if <paramref name="value"/> is a valid parameter value; otherwise, <c>false</c>.
    /// </returns>
    internal static bool IsValidParamValue(string value) => IsValid(value, "\"<>#&\\^`{|}");

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

    /// <summary>Checks if <paramref name="fragment"/> is a properly escaped URI fragment, i.e. it contains only
    ///  unreserved characters, reserved characters or '%'.</summary>
    /// <param name="fragment">The fragment to check.</param>
    /// <exception cref="FormatException">Thrown if the fragment is not valid.</exception>
    /// <remarks>The fragment of a URI with a supported protocol satisfies these requirements.</remarks>
    private static void CheckFragment(string fragment)
    {
        if (!IsValid(fragment, "\"<>\\^`{|}"))
        {
            throw new FormatException(
                @$"invalid fragment '{fragment
                }'; a valid fragment contains only unreserved characters, reserved characters or '%'");
        }
    }

    private static bool IsValid(string s, string invalidChars)
    {
        // The printable ASCII character range is x20 (space) to x7E inclusive. Space is an invalid character in
        // addition to the invalid characters in the invalidChars string.
        foreach (char c in s)
        {
            if (c.CompareTo('\x20') <= 0 ||
                c.CompareTo('\x7F') >= 0 ||
                invalidChars.Contains(c, StringComparison.InvariantCulture))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Checks if <paramref name="name"/> is not empty, not equal to <c>alt-server</c> nor equal to
    /// <c>transport</c> and contains only unreserved characters, <c>%</c>, or reserved characters other than <c>#</c>,
    /// <c>&#38;</c> and <c>=</c>.</summary>
    /// <param name="name">The name to check.</param>
    /// <returns><c>true</c> if <paramref name="name"/> is a valid parameter name; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>The range of valid names is much larger than the range of names you should use. For example, you
    /// should avoid parameter names with a <c>%</c> or <c>$</c> character, even though these characters are valid
    /// in a name.</remarks>
    private static bool IsValidParamName(string name) =>
        name.Length > 0 && name != "alt-server" && name != "transport" && IsValid(name, "\"<>#&=\\^`{|}");

    private void CheckSupportedProtocol(string propertyName)
    {
        if (Protocol is null)
        {
            throw new InvalidOperationException($"cannot set {propertyName} on a relative service address");
        }
        else if (!Protocol.IsSupported)
        {
            throw new InvalidOperationException($"cannot set {propertyName} on a '{Protocol}' service address");
        }
    }
}

/// <summary>The service address type converter specifies how to convert a string to a service address. It's used by
/// sub-systems such as the Microsoft ConfigurationBinder to bind string values to ServiceAddress properties.</summary>
public class ServiceAddressTypeConverter : TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc/>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string valueStr ? new ServiceAddress(new Uri(valueStr)) : base.ConvertFrom(context, culture, value);
}
