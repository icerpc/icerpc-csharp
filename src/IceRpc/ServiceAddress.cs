// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace IceRpc;

/// <summary>A service address corresponds to the URI of a service, parsed and processed for easier consumption by
/// interceptors, <see cref="ConnectionPool"/> and other elements of the invocation pipeline. It's used to construct
/// an <see cref="OutgoingRequest"/>.</summary>
public sealed record class ServiceAddress
{
    /// <summary>Gets or initializes the secondary endpoints of this service address.</summary>
    /// <value>The secondary endpoints of this service address.</value>
    public ImmutableList<Endpoint> AltEndpoints
    {
        get => _altEndpoints;

        init
        {
            CheckSupportedProtocol(nameof(AltEndpoints));

            if (value.Count > 0)
            {
                if (_endpoint is null)
                {
                    throw new InvalidOperationException(
                        $"cannot set {nameof(AltEndpoints)} when {nameof(Endpoint)} is empty");
                }

                if (value.Any(e => e.Protocol != Protocol))
                {
                    throw new ArgumentException(
                        $"the {nameof(AltEndpoints)} endpoints must use the service address's protocol {Protocol}",
                        nameof(value));
                }
            }
            // else, no need to check anything, an empty list is always fine.

            _altEndpoints = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets or initializes the main endpoint of this service address.</summary>
    /// <value>The main endpoint of this service address, or null if this service address has no endpoint.</value>
    public Endpoint? Endpoint
    {
        get => _endpoint;

        init
        {
            CheckSupportedProtocol(nameof(Endpoint));
            if (value?.Protocol is Protocol newProtocol && newProtocol != Protocol)
            {
                throw new ArgumentException(
                    $"the {nameof(Endpoint)} must use the service address's protocol: '{Protocol}'",
                    nameof(value));
            }

            if (value is not null)
            {
                if (_params.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"cannot set {nameof(Endpoint)} on a service address with parameters");
                }
            }
            else if (_altEndpoints.Count > 0)
            {
                throw new InvalidOperationException(
                    $"cannot clear {nameof(Endpoint)} when {nameof(AltEndpoints)} is not empty");
            }
            _endpoint = value;
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
    /// URI-derived properties such as <see cref="Endpoint"/> have not been updated.</summary>
    public Uri? OriginalUri { get; private set; }

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

    /// <summary>Gets or initializes the parameters of this service address. Always empty when <see cref="Endpoint"/> is not
    /// null.</summary>
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

            if (_endpoint is not null && value.Count > 0)
            {
                throw new InvalidOperationException(
                    $"cannot set {nameof(Params)} on a service address with an endpoint");
            }

            _params = value;
            OriginalUri = null;
        }
    }

    /// <summary>Gets the protocol of this service address.</summary>
    /// <value>The protocol of the service address. It corresponds to the URI scheme and is null for a relative service
    /// address.</value>
    public Protocol? Protocol { get; }

    private ImmutableList<Endpoint> _altEndpoints = ImmutableList<Endpoint>.Empty;
    private Endpoint? _endpoint;
    private string _fragment = "";
    private ImmutableDictionary<string, string> _params = ImmutableDictionary<string, string>.Empty;
    private string _path = "/";

    /// <summary>Creates a service address from a URI string.</summary>
    /// <param name="s">The string to parse.</param>
    /// <returns>The parsed service address.</returns>
    public static ServiceAddress Parse(string s)
    {
        try
        {
            return s.StartsWith('/') ? new ServiceAddress { Path = s } :
                new ServiceAddress(new Uri(s, UriKind.Absolute));
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"cannot parse URI '{s}'", ex);
        }
    }

    /// <summary>Tries to create a service address from a URI string.</summary>
    /// <param name="s">The URI string to parse.</param>
    /// <param name="serviceAddress">The parsed service address.</param>
    /// <returns><c>true</c> when the string is parsed successfully; otherwise, <c>false</c>.</returns>
    public static bool TryParse(string s, [NotNullWhen(true)] out ServiceAddress? serviceAddress)
    {
        try
        {
            serviceAddress = Parse(s);
            return true;
        }
        catch (FormatException)
        {
            serviceAddress = null;
            return false;
        }
    }

    /// <summary>Constructs a service address from a protocol.</summary>
    /// <param name="protocol">The protocol, or null for a relative service address.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="protocol"/> is not null or a supported protocol.
    /// </exception>
    public ServiceAddress(Protocol? protocol = null) =>
        Protocol = protocol is null || protocol.IsSupported ? protocol :
            throw new ArgumentException("protocol must be null or a supported protocol", nameof(protocol));

    /// <summary>Constructs a service address from a URI.</summary>
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
                Protocol.CheckPath(_path);
                if (!Protocol.HasFragment && _fragment.Length > 0)
                {
                    throw new ArgumentException(
                        $"cannot create an {Protocol} service address with a fragment",
                        nameof(uri));
                }

                (ImmutableDictionary<string, string> queryParams, string? altEndpointValue) = uri.ParseQuery();

                if (uri.Authority.Length > 0)
                {
                    if (uri.UserInfo.Length > 0)
                    {
                        throw new ArgumentException("cannot create an endpoint with a user info", nameof(uri));
                    }

                    string host = uri.IdnHost;
                    Debug.Assert(host.Length > 0); // the IdnHost provided by Uri is never empty

                    _endpoint = new Endpoint(
                        Protocol,
                        host,
                        port: checked((ushort)(uri.Port == -1 ? Protocol.DefaultUriPort : uri.Port)),
                        queryParams);

                    if (altEndpointValue is not null)
                    {
                        // Split and parse recursively each endpoint
                        foreach (string endpointStr in altEndpointValue.Split(','))
                        {
                            string altUriString = $"{uri.Scheme}://{endpointStr}";

                            // The separator for endpoint parameters in alt-endpoint is $, so we replace these '$'
                            // by '&' before sending the string to Endpoint.FromString which uses '&' as separator.
                            _altEndpoints = _altEndpoints.Add(
                                IceRpc.Endpoint.FromString(altUriString.Replace('$', '&')));
                        }
                    }
                }
                else
                {
                    if (!_path.StartsWith('/'))
                    {
                        throw new FormatException($"invalid path in service address URI '{uri.OriginalString}'");
                    }

                    if (altEndpointValue is not null)
                    {
                        throw new FormatException($"invalid alt-endpoint parameter in URI '{uri.OriginalString}'");
                    }

                    Protocol.CheckServiceAddressParams(queryParams);
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
            CheckPath(_path);
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
            Endpoint == other.Endpoint &&
            AltEndpoints.SequenceEqual(other.AltEndpoints) &&
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
        hash.Add(_endpoint);
        hash.Add(_altEndpoints.Count);
        return hash.ToHashCode();
    }

    /// <summary>Converts this service address into a string.</summary>
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

        if (Endpoint is Endpoint endpoint)
        {
            sb.AppendEndpoint(endpoint, Path);
            firstOption = endpoint.Params.Count == 0;
        }
        else
        {
            sb.Append(Protocol);
            sb.Append(':');
            sb.Append(Path);
        }

        if (AltEndpoints.Count > 0)
        {
            StartQueryOption(sb, ref firstOption);
            sb.Append("alt-endpoint=");
            for (int i = 0; i < AltEndpoints.Count; ++i)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.AppendEndpoint(AltEndpoints[i], path: "", includeScheme: false, paramSeparator: '$');
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

    /// <summary>"unchecked" constructor used by the Slice decoder when decoding a Slice1 encoded service address.
    /// </summary>
    internal ServiceAddress(
        Protocol protocol,
        string path,
        Endpoint? endpoint,
        ImmutableList<Endpoint> altEndpoints,
        ImmutableDictionary<string, string> serviceAddressParams,
        string fragment)
    {
        Protocol = protocol;
        _path = path;
        _endpoint = endpoint;
        _altEndpoints = altEndpoints;
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
                @$"invalid fragment '{fragment}'; a valid fragment contains only unreserved characters, reserved characters or '%'");
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

    /// <summary>Checks if <paramref name="name"/> is not empty nor equal to <c>alt-endpoint</c> and contains only
    /// unreserved characters, <c>%</c>, or reserved characters other than <c>#</c>, <c>&#38;</c> and <c>=</c>.
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns><c>true</c> if <paramref name="name"/> is a valid parameter name; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>The range of valid names is much larger than the range of names you should use. For example, you
    /// should avoid parameter names with a <c>%</c> or <c>$</c> character, even though these characters are valid
    /// in a name.</remarks>
    private static bool IsValidParamName(string name) =>
        name.Length > 0 && name != "alt-endpoint" && IsValid(name, "\"<>#&=\\^`{|}");

    /// <summary>Checks if <paramref name="value"/> contains only unreserved characters, <c>%</c>, or reserved
    /// characters other than <c>#</c> and <c>&#38;</c>.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns><c>true</c> if <paramref name="value"/> is a valid parameter value; otherwise, <c>false</c>.
    /// </returns>
    private static bool IsValidParamValue(string value) => IsValid(value, "\"<>#&\\^`{|}");

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
