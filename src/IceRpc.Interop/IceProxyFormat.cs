// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace IceRpc
{
    /// <summary>The ZeroC Ice stringified proxy format. For example, "fred:tcp -h localhost -p 10000".</summary>
    /// <remarks>When parsing, all Ice proxy formats are equivalent. The various instances (<see cref="ASCII"/>,
    /// <see cref="Compat"/> and <see cref="Unicode"/>) only affect the output of
    /// <see cref="Proxy.ToString(IProxyFormat)"/> and similar ToString methods.</remarks>
    public class IceProxyFormat : IProxyFormat
    {
        /// <summary>With this format, characters with ordinal values greater than 127 are encoded as universal
        /// character names in the resulting string: \\unnnn for BMP characters and \\Unnnnnnnn for non-BMP characters.
        /// Non-printable ASCII characters with ordinal values 127 and below are encoded as \\t, \\n (etc.)
        /// or \\unnnn. This is an optional format introduced in Ice 3.7.</summary>
        public static IceProxyFormat ASCII { get; } = new(EscapeMode.ASCII);

        /// <summary>With this format, characters with ordinal values greater than 127 are encoded as a sequence of
        /// UTF-8 bytes using octal escapes. Characters with ordinal values 127 and below are encoded as \\t, \\n (etc.)
        /// or an octal escape. This is the format used by Ice 3.6 and earlier Ice versions.</summary>
        public static IceProxyFormat Compat { get; } = new(EscapeMode.Compat);

        /// <summary>An alias for the <see cref="Unicode"/> format.</summary>
        public static IceProxyFormat Default => Unicode;

        /// <summary>With this format, characters with ordinal values greater than 127 are kept as-is in the resulting
        /// string. Non-printable ASCII characters with ordinal values 127 and below are encoded as \\t, \\n (etc.).
        /// This corresponds to the default format in Ice 3.7.</summary>
        public static IceProxyFormat Unicode { get; } = new(EscapeMode.Unicode);

        internal EscapeMode EscapeMode { get; }

        /// <inheritdoc/>
        public Proxy Parse(string s, IInvoker? invoker = null)
        {
            s = s.Trim();
            if (s.Length == 0)
            {
                throw new FormatException("an empty string does not represent a proxy");
            }

            int beg = 0;
            int end = 0;

            const string delim = " \t\n\r";

            // Extract the identity, which may be enclosed in single or double quotation marks.
            string identityString;
            end = StringUtil.CheckQuote(s, beg);
            if (end == -1)
            {
                throw new FormatException($"mismatched quotes around identity in '{s}'");
            }
            else if (end == 0)
            {
                end = StringUtil.FindFirstOf(s, delim + ":@", beg);
                if (end == -1)
                {
                    end = s.Length;
                }
                identityString = s[beg..end];
            }
            else
            {
                beg++; // Skip leading quote
                identityString = s[beg..end];
                end++; // Skip trailing quote
            }

            if (beg == end)
            {
                throw new FormatException($"no identity in '{s}'");
            }

            // Parsing the identity string may throw FormatException.
            string path = IdentityToPath(identityString);
            string facet = "";
            Encoding encoding = IceDefinitions.Encoding;
            Endpoint? endpoint = null;
            var altEndpoints = ImmutableList<Endpoint>.Empty;

            while (true)
            {
                beg = StringUtil.FindFirstNotOf(s, delim, end);
                if (beg == -1)
                {
                    break;
                }

                if (s[beg] == ':' || s[beg] == '@')
                {
                    break;
                }

                end = StringUtil.FindFirstOf(s, delim + ":@", beg);
                if (end == -1)
                {
                    end = s.Length;
                }

                if (beg == end)
                {
                    break;
                }

                string option = s[beg..end];
                if (option.Length != 2 || option[0] != '-')
                {
                    throw new FormatException($"expected a proxy option but found '{option}' in '{s}'");
                }

                // Check for the presence of an option argument. The argument may be enclosed in single or double
                // quotation marks.
                string? argument = null;
                int argumentBeg = StringUtil.FindFirstNotOf(s, delim, end);
                if (argumentBeg != -1)
                {
                    char ch = s[argumentBeg];
                    if (ch != '@' && ch != ':' && ch != '-')
                    {
                        beg = argumentBeg;
                        end = StringUtil.CheckQuote(s, beg);
                        if (end == -1)
                        {
                            throw new FormatException($"mismatched quotes around value for {option} option in '{s}'");
                        }
                        else if (end == 0)
                        {
                            end = StringUtil.FindFirstOf(s, delim + ":@", beg);
                            if (end == -1)
                            {
                                end = s.Length;
                            }
                            argument = s[beg..end];
                        }
                        else
                        {
                            beg++; // Skip leading quote
                            argument = s[beg..end];
                            end++; // Skip trailing quote
                        }
                    }
                }

                switch (option[1])
                {
                    case 'f':
                        if (argument == null)
                        {
                            throw new FormatException($"no argument provided for -f option in '{s}'");
                        }
                        facet = StringUtil.UnescapeString(argument, 0, argument.Length, "");
                        break;

                    // None of the "invocation mode" option has any effect with IceRPC
                    case 't':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument '{argument}' provided for -t option in '{s}'");
                        }
                        break;

                    case 'o':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument '{argument}' provided for -o option in '{s}'");
                        }
                        break;

                    case 'O':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument '{argument}' provided for -O option in '{s}'");
                        }
                        break;

                    case 'd':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument '{argument}' provided for -d option in '{s}'");
                        }
                        break;

                    case 'D':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument '{argument}' provided for -D option in '{s}'");
                        }
                        break;

                    case 's':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument '{argument}' provided for -s option in '{s}'");
                        }
                        break;

                    case 'e':
                        if (argument == null)
                        {
                            throw new FormatException($"no argument provided for -e option in '{s}'");
                        }
                        encoding = Encoding.FromString(argument);
                        try
                        {
                            _ = encoding.ToMajorMinor();
                        }
                        catch (NotSupportedException ex)
                        {
                            throw new FormatException(
                                $"argument for -e option in '{s}' is not in major.minor format", ex);
                        }
                        break;

                    case 'p':
                        if (argument == null)
                        {
                            throw new FormatException($"no argument provided for -p option in '{s}'");
                        }
                        if (argument != "1.0")
                        {
                            throw new FormatException($"invalid value for -p option in '{s}'");
                        }
                        break;

                    default:
                        throw new FormatException($"unknown option '{option}' in '{s}'");
                }
            }

            if (beg == -1)
            {
                // Well-known proxy
                return new Proxy(Protocol.Ice)
                {
                    Path = path,
                    Fragment = Uri.EscapeDataString(facet),
                    Invoker = invoker ?? Proxy.DefaultInvoker,
                    Encoding = encoding,
                };
            }

            if (s[beg] == ':')
            {
                end = beg;

                while (end < s.Length && s[end] == ':')
                {
                    beg = end + 1;

                    end = beg;
                    while (true)
                    {
                        end = s.IndexOf(':', end);
                        if (end == -1)
                        {
                            end = s.Length;
                            break;
                        }
                        else
                        {
                            bool quoted = false;
                            int quote = beg;
                            while (true)
                            {
                                quote = s.IndexOf('\"', quote);
                                if (quote == -1 || end < quote)
                                {
                                    break;
                                }
                                else
                                {
                                    quote = s.IndexOf('\"', ++quote);
                                    if (quote == -1)
                                    {
                                        break;
                                    }
                                    else if (end < quote)
                                    {
                                        quoted = true;
                                        break;
                                    }
                                    ++quote;
                                }
                            }
                            if (!quoted)
                            {
                                break;
                            }
                            ++end;
                        }
                    }

                    string es = s[beg..end];
                    try
                    {
                        if (endpoint == null)
                        {
                            endpoint = ParseEndpoint(es);
                        }
                        else
                        {
                            altEndpoints = altEndpoints.Add(ParseEndpoint(es));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Give context to the exception.
                        throw new FormatException($"failed to parse endpoint '{es}'", ex);
                    }
                }

                Debug.Assert(endpoint != null);

                return new Proxy(Protocol.Ice)
                {
                    Path = path,
                    Fragment = Uri.EscapeDataString(facet),
                    Invoker = invoker ?? Proxy.DefaultInvoker,
                    Endpoint = endpoint,
                    AltEndpoints = altEndpoints,
                    Encoding = encoding,
                };
            }
            else if (s[beg] == '@')
            {
                beg = StringUtil.FindFirstNotOf(s, delim, beg + 1);
                if (beg == -1)
                {
                    throw new FormatException($"missing adapter ID in '{s}'");
                }

                string adapterIdStr;
                end = StringUtil.CheckQuote(s, beg);
                if (end == -1)
                {
                    throw new FormatException($"mismatched quotes around adapter ID in '{s}'");
                }
                else if (end == 0)
                {
                    end = StringUtil.FindFirstOf(s, delim, beg);
                    if (end == -1)
                    {
                        end = s.Length;
                    }
                    adapterIdStr = s[beg..end];
                }
                else
                {
                    beg++; // Skip leading quote
                    adapterIdStr = s[beg..end];
                    end++; // Skip trailing quote
                }

                if (end != s.Length && StringUtil.FindFirstNotOf(s, delim, end) != -1)
                {
                    throw new FormatException(
                        $"invalid trailing characters after '{s.Substring(0, end + 1)}' in '{s}'");
                }

                string adapterId = StringUtil.UnescapeString(adapterIdStr, 0, adapterIdStr.Length, "");

                if (adapterId.Length == 0)
                {
                    throw new FormatException($"empty adapter ID in proxy '{s}'");
                }

                return new Proxy(Protocol.Ice)
                {
                    Path = path,
                    Fragment = Uri.EscapeDataString(facet),
                    Invoker = invoker ?? Proxy.DefaultInvoker,
                    Encoding = encoding,
                    Params = ImmutableDictionary<string, string>.Empty.Add("adapter-id", adapterId)
                };
            }

            throw new FormatException($"malformed proxy '{s}'");
        }

        /// <inheritdoc/>
        public string ToString(Proxy proxy)
        {
            if (proxy.Protocol != Protocol.Ice)
            {
                throw new NotSupportedException($"{nameof(ToString)} supports only ice proxies");
            }

            string facet = Uri.UnescapeDataString(proxy.Fragment);

            var sb = new StringBuilder();

            // We enclose the identity string in quotes when the identity string contains characters we use as
            // separators.
            string identity = PathToIdentity(proxy.Path, this);
            if (StringUtil.FindFirstOf(identity, " :@") != -1)
            {
                sb.Append('"');
                sb.Append(identity);
                sb.Append('"');
            }
            else
            {
                sb.Append(identity);
            }

            if (facet.Length > 0)
            {
                // If the encoded facet string contains characters which the reference parser uses as separators,
                // then we enclose the facet string in quotes.
                sb.Append(" -f ");
                string fs = StringUtil.EscapeString(facet, EscapeMode);
                if (StringUtil.FindFirstOf(fs, " :@") != -1)
                {
                    sb.Append('"');
                    sb.Append(fs);
                    sb.Append('"');
                }
                else
                {
                    sb.Append(fs);
                }
            }

            if (proxy.Endpoint is Endpoint endpoint &&
                endpoint.Params.TryGetValue("transport", out string? transport) &&
                transport == TransportNames.Udp)
            {
                sb.Append(" -d");
            }
            else
            {
                sb.Append(" -t");
            }

            // Always print the encoding version to ensure a stringified proxy will convert back to a proxy with the
            // same encoding with StringToProxy. (Only needed for backwards compatibility).
            sb.Append(" -e ");
            sb.Append(proxy.Encoding);

            if (proxy.Endpoint == null)
            {
                if (proxy.Params.TryGetValue("adapter-id", out string? adapterId))
                {
                    sb.Append(" @ ");

                    // If the encoded adapter ID contains characters which the proxy parser uses as separators, then
                    // we enclose the adapter ID string in double quotes.
                    adapterId = StringUtil.EscapeString(adapterId, EscapeMode);
                    if (StringUtil.FindFirstOf(adapterId, " :@") != -1)
                    {
                        sb.Append('"');
                        sb.Append(adapterId);
                        sb.Append('"');
                    }
                    else
                    {
                        sb.Append(adapterId);
                    }
                }
            }
            else
            {
                sb.Append(':');
                sb.Append(ToString(proxy.Endpoint.Value));

                foreach (Endpoint e in proxy.AltEndpoints)
                {
                    sb.Append(':');
                    sb.Append(ToString(e));
                }
            }
            return sb.ToString();
        }

        private static Endpoint ParseEndpoint(string endpointString)
        {
            string[]? args = StringUtil.SplitString(endpointString, " \t\r\n");
            if (args == null)
            {
                throw new FormatException($"mismatched quote in endpoint '{endpointString}'");
            }

            if (args.Length == 0)
            {
                throw new FormatException("no non-whitespace character in endpoint string");
            }

            string transport = args[0];
            if (transport.Length == 0 ||
                !char.IsLetter(transport, 0) ||
                !transport.Skip(1).All(c => char.IsLetterOrDigit(c)))
            {
                throw new FormatException($"invalid transport name '{transport}' in endpoint '{endpointString}");
            }

            string? host = null;
            ushort? port = null;

            // We map "default" to tcp for consistency with the Slice 1.1 Encoder/Decoder.
            var endpointParams = new Dictionary<string, string>
            {
                ["transport"] = transport == "default" ? TransportNames.Tcp : transport,
            };

            if (transport == TransportNames.Opaque)
            {
                host = OpaqueTransport.Host;
                port = OpaqueTransport.Port;
            }

            // Parse args into name/value pairs (and skip transport at args[0])
            for (int n = 1; n < args.Length; ++n)
            {
                // Any name with < 2 characters or that does not start with - is illegal
                string name = args[n];
                if (name.Length < 2 || name[0] != '-')
                {
                    throw new FormatException($"invalid parameter name '{name}' in endpoint '{endpointString}'");
                }

                // Extract the value given to the current parameter, if any
                string value = "";
                if (n + 1 < args.Length && args[n + 1][0] != '-')
                {
                    value = args[++n];
                }

                if (name == "-h")
                {
                    if (host != null)
                    {
                        throw new FormatException($"too many -h parameters in endpoint '{endpointString}'");
                    }
                    else if (value.Length == 0)
                    {
                        throw new FormatException($"invalid empty host value in endpoint '{endpointString}'");
                    }
                    else if (value == "*")
                    {
                        host = "::0";
                    }
                    else
                    {
                        host = value;
                    }
                }
                else if (name == "-p")
                {
                    if (port != null)
                    {
                        throw new FormatException($"too many -h parameters in endpoint '{endpointString}'");
                    }
                    else
                    {
                        try
                        {
                            port = ushort.Parse(value, CultureInfo.InvariantCulture);
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException(
                                $"invalid value for -p parameter in '{endpointString}', must be between 0 and 65535",
                                ex);
                        }
                    }
                }
                else
                {
                    if (name.StartsWith("--", StringComparison.Ordinal))
                    {
                        name = name[2..];
                    }
                    else if (name.StartsWith('-'))
                    {
                        name = name[1..];
                    }
                    endpointParams.Add(name, value);
                }
            }

            if (host == null)
            {
                throw new FormatException($"no -h in endpoint '{endpointString}'");
            }

            return new Endpoint(Protocol.Ice)
            {
                Host = host,
                Port = port ?? 0,
                Params = endpointParams.ToImmutableDictionary()
            };
        }

        /// <summary>Converts a stringified identity into a path.</summary>
        private static string IdentityToPath(string identityString)
        {
            // Find unescaped separator. Note that the string may contain an escaped backslash before the separator.
            int slash = -1, pos = 0;
            while ((pos = identityString.IndexOf('/', pos)) != -1)
            {
                int escapes = 0;
                while (pos - escapes > 0 && identityString[pos - escapes - 1] == '\\')
                {
                    escapes++;
                }

                // We ignore escaped escapes
                if (escapes % 2 == 0)
                {
                    if (slash == -1)
                    {
                        slash = pos;
                    }
                    else
                    {
                        // Extra unescaped slash found.
                        throw new FormatException($"unescaped backslash in identity '{identityString}'");
                    }
                }
                pos++;
            }

            string category;
            string? name = null;
            if (slash == -1)
            {
                try
                {
                    name = StringUtil.UnescapeString(identityString, 0, identityString.Length, "/");
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException($"invalid name in identity '{identityString}'", ex);
                }
                category = "";
            }
            else
            {
                try
                {
                    category = StringUtil.UnescapeString(identityString, 0, slash, "/");
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException($"invalid category in identity '{identityString}'", ex);
                }

                if (slash + 1 < identityString.Length)
                {
                    try
                    {
                        name = StringUtil.UnescapeString(identityString, slash + 1, identityString.Length, "/");
                    }
                    catch (ArgumentException ex)
                    {
                        throw new FormatException($"invalid name in identity '{identityString}'", ex);
                    }
                }
            }

            return name?.Length > 0 ? new Identity(name, category).ToPath() :
                throw new FormatException($"invalid empty name in identity '{identityString}'");

        }

        /// <summary>Converts path into a stringified identity.</summary>
        private static string PathToIdentity(string path, IceProxyFormat format)
        {
            var identity = Identity.Parse(path);

            if (identity.Name.Length == 0)
            {
                return "";
            }

            string escapedName = StringUtil.EscapeString(identity.Name, format.EscapeMode, '/');

            if (identity.Category.Length == 0)
            {
                return escapedName;
            }
            else
            {
                string escapedCategory = StringUtil.EscapeString(identity.Category, format.EscapeMode, '/');
                return $"{escapedCategory}/{escapedName}";
            }
        }

        /// <summary>Converts an endpoint into a string in a format compatible with ZeroC Ice.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The string representation of this endpoint.</returns>
        private static string ToString(Endpoint endpoint)
        {
            var sb = new StringBuilder();

            // The default transport is "tcp" for consistency with the Slice 1.1 Encoder/Decoder.
            string transport = endpoint.Params.TryGetValue("transport", out string? transportValue) ?
                transportValue : TransportNames.Tcp;

            sb.Append(transport);

            if (transport != TransportNames.Opaque) // no -h -p for opaque
            {
                if (endpoint.Host.Length > 0)
                {
                    sb.Append(" -h ");
                    bool addQuote = endpoint.Host.IndexOf(':', StringComparison.Ordinal) != -1;
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                    sb.Append(endpoint.Host);
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                }

                if (endpoint.Port != 0)
                {
                    sb.Append(" -p ");
                    sb.Append(endpoint.Port.ToString(CultureInfo.InvariantCulture));
                }
            }

            foreach ((string name, string value) in endpoint.Params)
            {
                if (name != "transport")
                {
                    sb.Append(' ');

                    // Add - or -- prefix as appropriate
                    sb.Append('-');
                    if (name.Length > 1)
                    {
                        sb.Append('-');
                    }
                    sb.Append(name);
                    if (value.Length > 0)
                    {
                        sb.Append(' ');
                        sb.Append(value);
                    }
                }
            }
            return sb.ToString();
        }

        private IceProxyFormat(EscapeMode escapeMode) => EscapeMode = escapeMode;
    }
}
