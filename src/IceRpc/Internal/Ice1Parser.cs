// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;

namespace IceRpc.Internal
{
    /// <summary>Provides helper methods to parse proxy and endpoint strings in the ice1 format.</summary>
    internal static class Ice1Parser
    {
        internal const ushort DefaultPort = 0;

        internal static Endpoint ParseEndpointString(string endpointString)
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

            string transportName = args[0];
            if (transportName == "default")
            {
                transportName = "tcp";
            }
            else if (transportName.Length == 0 ||
                    !char.IsLetter(transportName, 0) ||
                    !transportName.Skip(1).All(c => char.IsLetterOrDigit(c)))
            {
                throw new FormatException($"invalid transport name '{transportName}' in endpoint '{endpointString}");
            }

            string? host = null;
            ushort? port = null;
            var endpointParams = new List<EndpointParam>();

            // Parse args into name/value pairs (and skip transportName at args[0])
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
                    endpointParams.Add(new EndpointParam(name, value));
                }
            }

            return new Endpoint(Protocol.Ice1,
                                transportName,
                                host ?? "",
                                port ?? 0,
                                endpointParams.ToImmutableList());
        }

        /// <summary>Parses a proxy string in the ice1 format.</summary>
        /// <param name="s">The string to parse.</param>
        /// <returns>The arguments to create the proxy.</returns>
        internal static Proxy ParseProxyString(string s)
        {
            // TODO: rework this implementation

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

            // Parsing the identity may throw FormatException.
            var identity = Identity.Parse(identityString);
            string facet = "";
            Encoding encoding = Ice1Definitions.Encoding;
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
                return new Proxy(new IdentityAndFacet(identity, facet).ToPath(), Protocol.Ice1) { Encoding = encoding };
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
                            endpoint = ParseEndpointString(es);

                            if (endpoint.Transport == TransportNames.Loc)
                            {
                                throw new FormatException("use @ adapterId instead of loc in proxy");
                            }
                        }
                        else
                        {
                            altEndpoints = altEndpoints.Add(ParseEndpointString(es));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Give context to the exception.
                        throw new FormatException($"failed to parse endpoint '{es}'", ex);
                    }
                }

                Debug.Assert(endpoint != null);

                return new Proxy(new IdentityAndFacet(identity, facet).ToPath(), Protocol.Ice1)
                {
                    Endpoint = endpoint,
                    AltEndpoints = altEndpoints,
                    Encoding = encoding
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

                endpoint = new Endpoint(Protocol.Ice1,
                                        TransportNames.Loc,
                                        host: adapterId,
                                        port: 0,
                                        ImmutableList<EndpointParam>.Empty);

                return new Proxy(new IdentityAndFacet(identity, facet).ToPath(), Protocol.Ice1)
                {
                    Endpoint = endpoint,
                    Encoding = encoding
                };
            }

            throw new FormatException($"malformed proxy '{s}'");
        }
    }
}
