// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IceRpc.Internal
{
    /// <summary>Provides helper methods to parse proxy and endpoint strings in the ice1 format.</summary>
    internal static class Ice1Parser
    {
        internal static EndpointRecord ParseEndpointRecord(string endpointString)
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
            var parameters = new List<EndpointParameter>();
            var localParameters = new List<EndpointParameter>();

            // Parse args into name/value pairs (and skip transportName at args[0])
            for (int n = 1; n < args.Length; ++n)
            {
                // Any name with < 2 characters or that does not start with - is illegal
                string name = args[n];
                if (name.Length < 2 || name[0] != '-')
                {
                    throw new FormatException($"invalid parameter name '{name}' in endpoint '{endpointString}'");
                }

                // Extract the value given to the current option, if any
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
                        port = ushort.Parse(value, CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    if (name.StartsWith("--", StringComparison.Ordinal))
                    {
                        localParameters.Add(new EndpointParameter(name, value));
                    }
                    else
                    {
                        parameters.Add(new EndpointParameter(name, value));
                    }
                }
            }

            return new EndpointRecord(Protocol.Ice1,
                                      transportName,
                                      host ?? "",
                                      port ?? 0,
                                      parameters.ToImmutableList(),
                                      localParameters.ToImmutableList());
        }

        /// <summary>Parses compress (-z) from an ice1 options dictionary.</summary>
        internal static bool ParseCompress(Dictionary<string, string?> options, string endpointString)
        {
            bool compress = false;

            if (options.TryGetValue("-z", out string? argument))
            {
                if (argument != null)
                {
                    throw new FormatException(
                        $"unexpected argument '{argument}' provided for -z option in '{endpointString}'");
                }
                compress = true;
                options.Remove("-z");
            }
            return compress;
        }

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

            if (TransportRegistry.TryGetValue(transportName, out IEndpointFactory? factory) &&
                factory is IIce1EndpointFactory ice1EndpointFactory)
            {
                Endpoint endpoint = ice1EndpointFactory.CreateIce1Endpoint(options, endpointString);
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

                return opaqueEndpoint;

                // TODO: rework this code

                /*
                if (opaqueEndpoint.ValueEncoding.IsSupported &&
                    TransportRegistry.TryGetValue(opaqueEndpoint.TransportCode, out factory) &&
                    factory is IIce1EndpointFactory)
                {
                    // We may be able to unmarshal this endpoint, so we first marshal it into a byte buffer and then
                    // unmarshal it from this buffer.
                    // 8 = size of short + size of 1.1 encapsulation header
                    var buffer = new byte[8 + opaqueEndpoint.Value.Length];
                    var encoder = new IceEncoder(Ice1Definitions.Encoding, buffer);
                    encoder.EncodeEndpoint11(opaqueEndpoint);
                    ReadOnlyMemory<byte> readBuffer = encoder.Finish().ToSingleBuffer();
                    Debug.Assert(encoder.Tail.Buffer == 0 && encoder.Tail.Offset == 8 + opaqueEndpoint.Value.Length);

                    return new IceDecoder(readBuffer, Ice1Definitions.Encoding).DecodeEndpoint11(Protocol.Ice1);
                }
                else
                {
                    return opaqueEndpoint;
                }
                */
            }

            throw new FormatException($"unknown transport '{transportName}' in endpoint '{endpointString}'");
        }

        /// <summary>Parses host (-h) and port (-p) from an ice1 options dictionary.</summary>
        /// <param name="options">The options parsed from the endpoint string.</param>
        /// <param name="endpointString">The source endpoint string.</param>
        /// <returns>The host and port extracted from <paramref name="options"/>.</returns>
        internal static (string Host, ushort Port) ParseHostAndPort(
            Dictionary<string, string?> options,
            string endpointString)
        {
            string host;
            ushort port = 0;

            if (options.TryGetValue("-h", out string? argument))
            {
                host = argument ??
                    throw new FormatException($"no argument provided for -h option in endpoint '{endpointString}'");

                if (host == "*")
                {
                    // TODO: Should we check that IPv6 is enabled first and use 0.0.0.0 otherwise, or will
                    // ::0 just bind to the IPv4 addresses in this case?
                    host = "::0";
                }
                options.Remove("-h");
            }
            else
            {
                throw new FormatException($"no -h option in endpoint '{endpointString}'");
            }

            if (options.TryGetValue("-p", out argument))
            {
                if (argument == null)
                {
                    throw new FormatException($"no argument provided for -p option in endpoint '{endpointString}'");
                }

                try
                {
                    port = ushort.Parse(argument, CultureInfo.InvariantCulture);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"invalid port value '{argument}' in endpoint '{endpointString}'", ex);
                }
                options.Remove("-p");
            }
            // else port remains 0

            return (host, port);
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
            EndpointRecord? endpoint = null;
            var altEndpoints = ImmutableList<EndpointRecord>.Empty;

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
                return new Proxy(identity, facet)
                {
                    Encoding = encoding
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
                            endpoint = ParseEndpointRecord(es);

                            if (endpoint.Transport == TransportNames.Loc)
                            {
                                throw new FormatException("use @ adapterId instead of loc in proxy");
                            }
                        }
                        else
                        {
                            altEndpoints = altEndpoints.Add(ParseEndpointRecord(es));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Give context to the exception.
                        throw new FormatException($"failed to parse endpoint '{es}'", ex);
                    }
                }

                Debug.Assert(endpoint != null);

                return new Proxy(identity, facet)
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

                endpoint = new EndpointRecord(Protocol.Ice1,
                                              TransportNames.Loc,
                                              Host: adapterId,
                                              Port: 0,
                                              ImmutableList<EndpointParameter>.Empty,
                                              ImmutableList<EndpointParameter>.Empty);

                return new Proxy(identity, facet)
                {
                    Endpoint = endpoint,
                    Encoding = encoding
                };
            }

            throw new FormatException($"malformed proxy '{s}'");
        }

        /// <summary>Parses a proxy string in the ice1 format.</summary>
        /// <param name="s">The string to parse.</param>
        /// <returns>The arguments to create the proxy.</returns>
        internal static (Identity Identity, string Facet, Encoding Encoding, Endpoint? Endpoint, ImmutableList<Endpoint> AltEndpoints) ParseProxy(
            string s)
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
                return (identity, facet, encoding, endpoint, altEndpoints);
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

                            if (endpoint.TransportCode == TransportCode.Loc)
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

                return (identity, facet, encoding, endpoint, altEndpoints);
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
                return (identity, facet, encoding, endpoint, altEndpoints);
            }

            throw new FormatException($"malformed proxy '{s}'");
        }

        /// <summary>Parses a timeout from an options dictionary.</summary>
        internal static TimeSpan ParseTimeout(
            Dictionary<string, string?> options,
            TimeSpan defaultTimeout,
            string endpointString)
        {
            TimeSpan timeout = defaultTimeout;

            if (options.TryGetValue("-t", out string? argument))
            {
                if (argument == null)
                {
                    throw new FormatException($"no argument provided for -t option in endpoint '{endpointString}'");
                }
                if (argument == "infinite")
                {
                    timeout = System.Threading.Timeout.InfiniteTimeSpan;
                }
                else
                {
                    try
                    {
                        timeout = TimeSpan.FromMilliseconds(int.Parse(argument, CultureInfo.InvariantCulture));
                    }
                    catch (FormatException ex)
                    {
                        throw new FormatException(
                            $"invalid timeout value '{argument}' in endpoint '{endpointString}'",
                            ex);
                    }
                    if (timeout <= TimeSpan.Zero)
                    {
                        throw new FormatException(
                            $"invalid timeout value '{argument}' in endpoint '{endpointString}'");
                    }
                }
                options.Remove("-t");
            }
            return timeout;
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
