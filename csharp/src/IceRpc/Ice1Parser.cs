// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop.ZeroC.Ice;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace IceRpc
{
    /// <summary>Provides helper methods to parse proxy and endpoint strings in the ice1 format.</summary>
    internal static class Ice1Parser
    {
        /// <summary>Parses a string that represents one or more endpoints.</summary>
        /// <param name="endpointString">The string to parse.</param>
        /// <param name="communicator">The communicator.</param>
        /// <param name="serverEndpoints">When true (the default), endpointString corresponds to the Endpoints property of
        /// a server. Otherwise, false.</param>
        /// <returns>The list of endpoints.</returns>
        internal static IReadOnlyList<Endpoint> ParseEndpoints(
            string endpointString,
            Communicator communicator,
            bool serverEndpoints = true)
        {
            int beg;
            int end = 0;

            string delim = " \t\n\r";

            var endpoints = new List<Endpoint>();
            while (end < endpointString.Length)
            {
                beg = StringUtil.FindFirstNotOf(endpointString, delim, end);
                if (beg == -1)
                {
                    if (endpoints.Count != 0)
                    {
                        throw new FormatException("invalid empty server endpoint");
                    }
                    break;
                }

                end = beg;
                while (true)
                {
                    end = endpointString.IndexOf(':', end);
                    if (end == -1)
                    {
                        end = endpointString.Length;
                        break;
                    }
                    else
                    {
                        bool quoted = false;
                        int quote = beg;
                        while (true)
                        {
                            quote = endpointString.IndexOf('\"', quote);
                            if (quote == -1 || end < quote)
                            {
                                break;
                            }
                            else
                            {
                                quote = endpointString.IndexOf('\"', ++quote);
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

                if (end == beg)
                {
                    throw new FormatException("invalid empty server endpoint");
                }

                string s = endpointString[beg..end];
                endpoints.Add(CreateEndpoint(s, communicator, serverEndpoints));
                ++end;
            }

            return endpoints;
        }

        /// <summary>Parses a proxy string in the ice1 format.</summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="communicator">The communicator.</param>
        /// <returns>The components of the proxy.</returns>
        internal static (Identity Identity, string Facet, Encoding Encoding, string Location0, IReadOnlyList<Endpoint> Endpoints, bool Oneway) ParseProxy(
            string s,
            Communicator communicator)
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
                throw new FormatException($"mismatched quotes around identity in `{s}'");
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
                throw new FormatException($"no identity in `{s}'");
            }

            // Parsing the identity may raise FormatException.
            Identity identity = Identity.Parse(identityString);

            string facet = "";
            Encoding encoding = Ice1Definitions.Encoding;
            bool oneway = false;

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
                    throw new FormatException($"expected a proxy option but found `{option}' in `{s}'");
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
                            throw new FormatException($"mismatched quotes around value for {option} option in `{s}'");
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
                            throw new FormatException($"no argument provided for -f option in `{s}'");
                        }
                        facet = StringUtil.UnescapeString(argument, 0, argument.Length, "");
                        break;

                    case 't':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument `{argument}' provided for -t option in `{s}'");
                        }
                        break;

                    case 'o':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument `{argument}' provided for -o option in `{s}'");
                        }
                        oneway = true;
                        break;

                    case 'O':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument `{argument}' provided for -O option in `{s}'");
                        }

                        oneway = true;
                        break;

                    case 'd':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument `{argument}' provided for -d option in `{s}'");
                        }
                        oneway = true;
                        break;

                    case 'D':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument `{argument}' provided for -D option in `{s}'");
                        }
                        oneway = true;
                        break;

                    case 's':
                        if (argument != null)
                        {
                            throw new FormatException(
                                $"unexpected argument `{argument}' provided for -s option in `{s}'");
                        }

                        if (communicator.Logger.IsEnabled(LogLevel.Warning))
                        {
                            communicator.Logger.LogWarnProxySecureOptionHasNoEffect(s);
                        }
                        break;

                    case 'e':
                        if (argument == null)
                        {
                            throw new FormatException($"no argument provided for -e option in `{s}'");
                        }
                        encoding = Encoding.Parse(argument);
                        break;

                    case 'p':
                        if (argument == null)
                        {
                            throw new FormatException($"no argument provided for -p option in `{s}'");
                        }
                        if (argument != "1.0")
                        {
                            throw new FormatException($"invalid value for -p option in `{s}'");
                        }
                        break;

                    default:
                        throw new FormatException($"unknown option `{option}' in `{s}'");
                }
            }

            if (beg == -1)
            {
                return (identity, facet, encoding, Location0: "", ImmutableArray<Endpoint>.Empty, oneway);
            }

            var endpoints = new List<Endpoint>();

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
                    endpoints.Add(CreateEndpoint(es, communicator, false));
                }

                Debug.Assert(endpoints.Count > 0);
                return (identity, facet, encoding, Location0: "", endpoints, oneway);
            }
            else if (s[beg] == '@')
            {
                beg = StringUtil.FindFirstNotOf(s, delim, beg + 1);
                if (beg == -1)
                {
                    throw new FormatException($"missing adapter ID in `{s}'");
                }

                string locationStr;
                end = StringUtil.CheckQuote(s, beg);
                if (end == -1)
                {
                    throw new FormatException($"mismatched quotes around adapter ID in `{s}'");
                }
                else if (end == 0)
                {
                    end = StringUtil.FindFirstOf(s, delim, beg);
                    if (end == -1)
                    {
                        end = s.Length;
                    }
                    locationStr = s[beg..end];
                }
                else
                {
                    beg++; // Skip leading quote
                    locationStr = s[beg..end];
                    end++; // Skip trailing quote
                }

                if (end != s.Length && StringUtil.FindFirstNotOf(s, delim, end) != -1)
                {
                    throw new FormatException(
                        $"invalid trailing characters after `{s.Substring(0, end + 1)}' in `{s}'");
                }

                string location0 = StringUtil.UnescapeString(locationStr, 0, locationStr.Length, "");

                if (location0.Length == 0)
                {
                    throw new FormatException($"empty location in proxy `{s}'");
                }

                return (identity, facet, encoding, location0, ImmutableArray<Endpoint>.Empty, oneway);
            }

            throw new FormatException($"malformed proxy `{s}'");
        }

        /// <summary>Creates an endpoint from a string in the ice1 format.</summary>
        /// <param name="endpointString">The string parsed by this method.</param>
        /// <param name="communicator">The communicator of the enclosing proxy or server.</param>
        /// <param name="serverEndpoint">When true, endpointString represents a server's endpoint configuration;
        /// when false, endpointString represents a proxy endpoint.</param>
        /// <returns>The new endpoint.</returns>
        /// <exception cref="FormatException">Thrown when endpointString cannot be parsed.</exception>
        /// <exception cref="NotSupportedException">Thrown when the transport specified in endpointString does not
        /// the ice1 protocol.</exception>
        private static Endpoint CreateEndpoint(string endpointString, Communicator communicator, bool serverEndpoint)
        {
            string[]? args = StringUtil.SplitString(endpointString, " \t\r\n");
            if (args == null)
            {
                throw new FormatException($"mismatched quote in endpoint `{endpointString}'");
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
                    throw new FormatException($"invalid option `{option}' in endpoint `{endpointString}'");
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
                    throw new FormatException($"duplicate option `{option}' in endpoint `{endpointString}'");
                }
            }

            if (communicator.FindIce1EndpointParser(transportName) is (Ice1EndpointParser parser, Transport transport))
            {
                Endpoint endpoint = parser(transport, options, communicator, serverEndpoint, endpointString);
                if (options.Count > 0)
                {
                    throw new FormatException(
                        $"unrecognized option(s) `{ToString(options)}' in endpoint `{endpointString}'");
                }
                return endpoint;
            }

            // If the stringified endpoint is opaque, create an unknown endpoint, then see whether the type matches one
            // of the known endpoints.
            if (!serverEndpoint && transportName == "opaque")
            {
                var opaqueEndpoint = OpaqueEndpoint.Parse(options, communicator, endpointString);
                if (options.Count > 0)
                {
                    throw new FormatException(
                        $"unrecognized option(s) `{ToString(options)}' in endpoint `{endpointString}'");
                }

                if (opaqueEndpoint.ValueEncoding.IsSupported &&
                    communicator.FindIce1EndpointFactory(opaqueEndpoint.Transport) != null)
                {
                    // We may be able to unmarshal this endpoint, so we first marshal it into a byte buffer and then
                    // unmarshal it from this buffer.
                    var bufferList = new List<ArraySegment<byte>>
                    {
                        // 8 = size of short + size of encapsulation header with 1.1 encoding
                        new byte[8 + opaqueEndpoint.Value.Length]
                    };

                    var ostr = new OutputStream(Ice1Definitions.Encoding, bufferList);
                    ostr.WriteEndpoint(opaqueEndpoint);
                    ostr.Finish();
                    Debug.Assert(bufferList.Count == 1);
                    Debug.Assert(ostr.Tail.Segment == 0 && ostr.Tail.Offset == 8 + opaqueEndpoint.Value.Length);

                    return new InputStream(bufferList[0], Ice1Definitions.Encoding, communicator).
                        ReadEndpoint(Protocol.Ice1);
                }
                else
                {
                    return opaqueEndpoint;
                }
            }

            throw new FormatException($"unknown transport `{transportName}' in endpoint `{endpointString}'");
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
