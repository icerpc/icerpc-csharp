// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace IceRpc
{
    /// <summary>Provides helper methods to parse proxy and endpoint strings in the ice1 format.</summary>
    internal static class Ice1Parser
    {
        /// <summary>Creates an endpoint from a string in the ice1 format.</summary>
        /// <param name="endpointString">The string parsed by this method.</param>
        /// <returns>The new endpoint.</returns>
        /// <exception cref="FormatException">Thrown when endpointString cannot be parsed.</exception>
        internal static Endpoint ParseEndpoint(string endpointString)
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

            var options = new Dictionary<string, string?>();

            // Parse args into options (and skip transportName at args[0])
            for (int n = 1; n < args.Length; ++n)
            {
                // Any option with < 2 characters or that does not start with - is illegal
                string option = args[n];
                if (option.Length < 2 || option[0] != '-')
                {
                    throw new FormatException($"invalid option '{option}' in endpoint '{endpointString}'");
                }

                // Extract the argument given to the current option, if any
                string? argument = null;
                if (n + 1 < args.Length && args[n + 1][0] != '-')
                {
                    argument = args[++n];
                }

                try
                {
                    options.Add(option, argument);
                }
                catch (ArgumentException)
                {
                    throw new FormatException($"duplicate option '{option}' in endpoint '{endpointString}'");
                }
            }

            if (Runtime.FindIce1EndpointParser(transportName) is (Ice1EndpointParser parser, Transport transport))
            {
                Endpoint endpoint = parser(transport, options, endpointString);
                if (options.Count > 0)
                {
                    throw new FormatException(
                        $"unrecognized option(s) '{ToString(options)}' in endpoint '{endpointString}'");
                }
                return endpoint;
            }

            // If the stringified endpoint is opaque, create an unknown endpoint, then see whether the type matches one
            // of the known endpoints.
            if (transportName == "opaque")
            {
                var opaqueEndpoint = OpaqueEndpoint.Parse(options, endpointString);
                if (options.Count > 0)
                {
                    throw new FormatException(
                        $"unrecognized option(s) '{ToString(options)}' in endpoint '{endpointString}'");
                }

                if (opaqueEndpoint.ValueEncoding.IsSupported &&
                    Runtime.FindIce1EndpointFactory(opaqueEndpoint.Transport) != null)
                {
                    // We may be able to unmarshal this endpoint, so we first marshal it into a byte buffer and then
                    // unmarshal it from this buffer.
                    var bufferList = new List<ArraySegment<byte>>
                    {
                        // 8 = size of short + size of encapsulation header with 1.1 encoding
                        new byte[8 + opaqueEndpoint.Value.Length]
                    };

                    var ostr = new OutputStream(Ice1Definitions.Encoding, bufferList);
                    ostr.WriteEndpoint11(opaqueEndpoint);
                    ostr.Finish();
                    Debug.Assert(bufferList.Count == 1);
                    Debug.Assert(ostr.Tail.Segment == 0 && ostr.Tail.Offset == 8 + opaqueEndpoint.Value.Length);

                    return new InputStream(bufferList[0], Ice1Definitions.Encoding).ReadEndpoint11(Protocol.Ice1);
                }
                else
                {
                    return opaqueEndpoint;
                }
            }

            throw new FormatException($"unknown transport '{transportName}' in endpoint '{endpointString}'");
        }

        /// <summary>Parses a proxy string in the ice1 format.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="proxyOptions">The proxy options.</param>
        /// <returns>The arguments to create the proxy.</returns>
        internal static (Identity Identity, string Facet, Encoding Encoding, Endpoint? Endpoint, ImmutableList<Endpoint> AltEndpoints, ProxyOptions Options) ParseProxy(
            string s,
            ProxyOptions proxyOptions)
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
            proxyOptions = proxyOptions.Clone();

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
                        proxyOptions.IsOneway = true;
                        break;

                    case 'O':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument '{argument}' provided for -O option in '{s}'");
                        }

                        proxyOptions.IsOneway = true;
                        break;

                    case 'd':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument '{argument}' provided for -d option in '{s}'");
                        }
                        proxyOptions.IsOneway = true;
                        break;

                    case 'D':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument '{argument}' provided for -D option in '{s}'");
                        }
                        proxyOptions.IsOneway = true;
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
                        encoding = Encoding.Parse(argument);
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
                return (identity, facet, encoding, endpoint, altEndpoints, proxyOptions);
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

                            if (endpoint.Transport == Transport.Loc)
                            {
                                throw new FormatException("use @ adapterId instead of loc in proxy");
                            }
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

                if (endpoint.IsDatagram && altEndpoints.All(e => e.IsDatagram))
                {
                    proxyOptions.IsOneway = true;
                }

                return (identity, facet, encoding, endpoint, altEndpoints, proxyOptions);
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

                endpoint = LocEndpoint.Create(adapterId, Protocol.Ice1);
                return (identity, facet, encoding, endpoint, altEndpoints, proxyOptions);
            }

            throw new FormatException($"malformed proxy '{s}'");
        }

        // Stringify the options of an endpoint
        private static string ToString(Dictionary<string, string?> options)
        {
            var sb = new StringBuilder();
            foreach ((string option, string? argument) in options)
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(option);
                if (argument != null)
                {
                    sb.Append(' ');
                    sb.Append(argument);
                }
            }
            return sb.ToString();
        }
    }
}
