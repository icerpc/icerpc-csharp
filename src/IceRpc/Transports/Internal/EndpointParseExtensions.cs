// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Globalization;
using System.Net;

namespace IceRpc.Transports.Internal
{
    /// <summary>Various parse extension methods for class Endpoint.</summary>
    internal static class EndpointParseExtensions
    {
        internal const int DefaultTcpTimeout = 60_000;

        internal static (TransportCode TransportCode, ReadOnlyMemory<byte> Bytes) ParseExternalOpaqueParams(
           this Endpoint endpoint)
        {
            if (endpoint.Protocol != Protocol.Ice1)
            {
                throw new FormatException($"endpoint '{endpoint}': protocol/transport mistmatch");
            }

            TransportCode? transportCode = null;
            ReadOnlyMemory<byte> bytes = default;
            bool encodingFound = false;

            foreach ((string name, string value) in endpoint.ExternalParams)
            {
                switch (name)
                {
                    case "-e":
                        if (encodingFound)
                        {
                            throw new FormatException($"multiple -e parameters in endpoint '{endpoint}'");
                        }
                        if (value == "1.0" || value == "1.1")
                        {
                            encodingFound = true;
                        }
                        else
                        {
                            throw new FormatException($"invalid value for -e parameter in endpoint '{endpoint}'");
                        }
                        break;

                    case "-t":
                        if (transportCode != null)
                        {
                            throw new FormatException($"multiple -t parameters in endpoint '{endpoint}'");
                        }

                        short t;
                        try
                        {
                            t = short.Parse(value, CultureInfo.InvariantCulture);
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException(
                                $"invalid value for paramater -t in endpoint '{endpoint}'", ex);
                        }

                        if (t < 0)
                        {
                            throw new FormatException(
                                $"value for -t parameter out of range in endpoint '{endpoint}'");
                        }

                        transportCode = (TransportCode)t;
                        break;

                    case "-v":
                        if (bytes.Length > 0)
                        {
                            throw new FormatException($"multiple -v parameters in endpoint '{endpoint}'");
                        }

                        try
                        {
                            bytes = Convert.FromBase64String(value);
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException($"invalid Base64 value in endpoint '{endpoint}'", ex);
                        }
                        break;

                    default:
                        throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
                }
            }

            if (transportCode == null)
            {
                throw new FormatException($"missing -t parameter in endpoint '{endpoint}'");
            }
            else if (bytes.Length == 0)
            {
                throw new FormatException($"missing -v parameter in endpoint '{endpoint}'");
            }

            return (transportCode.Value, bytes);
        }

        internal static (bool Compress, int Timeout, bool? Tls) ParseTcpParams(this Endpoint endpoint)
        {
            bool? tls = null;

            foreach ((string name, string value) in endpoint.LocalParams)
            {
                if (endpoint.Protocol != Protocol.Ice1 && name == "_tls")
                {
                    if (tls != null)
                    {
                        throw new FormatException($"multiple _tls parameters in endpoint '{endpoint}'");
                    }
                    try
                    {
                        tls = bool.Parse(value);
                    }
                    catch (FormatException ex)
                    {
                        throw new FormatException($"invalid value for _tls parameter in endpoint '{endpoint}'", ex);
                    }
                }
                else
                {
                    throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
                }
            }

            (bool compress, int timeout) = endpoint.ParseExternalTcpParams();
            return (compress, timeout, tls);
        }

        internal static (bool Compress, int Timeout) ParseExternalTcpParams(this Endpoint endpoint)
        {
            bool compress = false;
            int? timeout = null;

            foreach ((string name, string value) in endpoint.ExternalParams)
            {
                if (endpoint.Protocol == Protocol.Ice1)
                {
                    switch (name)
                    {
                        case "-t":
                            if (timeout != null)
                            {
                                throw new FormatException($"multiple -t parameters in endpoint '{endpoint}'");
                            }
                            if (value == "infinite")
                            {
                                timeout = -1;
                            }
                            else
                            {
                                timeout = int.Parse(value, CultureInfo.InvariantCulture); // timeout in ms, or -1
                                if (timeout == 0 || timeout < -1)
                                {
                                    throw new FormatException(
                                        $"invalid value for -t parameter in endpoint '{endpoint}'");
                                }
                            }
                            continue; // loop back

                        case "-z":
                            if (compress)
                            {
                                throw new FormatException($"multiple -z parameters in endpoint '{endpoint}'");
                            }
                            if (value.Length > 0)
                            {
                                throw new FormatException(
                                    $"invalid value '{value}' for parameter -z in endpoint '{endpoint}'");
                            }
                            compress = true;
                            continue; // loop back

                        default:
                            break;
                    }

                    throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
                }
            }
            return (compress, timeout ?? DefaultTcpTimeout);
        }

        internal static (bool Compress, int Ttl, string? MulticastInterface) ParseUdpParams(this Endpoint endpoint)
        {
            int ttl = -1;
            string? multicastInterface = null;

            foreach ((string name, string value) in endpoint.LocalParams)
            {
                switch (name)
                {
                    case "--ttl":
                        if (ttl >= 0)
                        {
                            throw new FormatException($"multiple --ttl parameters in endpoint '{endpoint}'");
                        }

                        if (value.Length == 0)
                        {
                            throw new FormatException(
                                $"no value provided for --ttl parameter in endpoint '{endpoint}'");
                        }
                        try
                        {
                            ttl = int.Parse(value, CultureInfo.InvariantCulture);
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException($"invalid TTL value '{value}' in endpoint '{endpoint}'", ex);
                        }

                        if (ttl < 0)
                        {
                            throw new FormatException(
                                $"TTL value '{value}' out of range in endpoint '{endpoint}'");
                        }
                        break;

                    case "--interface":
                        if (multicastInterface != null)
                        {
                            throw new FormatException($"multiple --interface parameters in endpoint '{endpoint}'");
                        }
                        if (value.Length == 0)
                        {
                            throw new FormatException(
                                $"no value provided for --interface parameter in endpoint '{endpoint}'");
                        }
                        if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress) ||
                            !UdpUtils.IsMulticast(ipAddress))
                        {
                            throw new FormatException(@$"--interface parameter in endpoint '{endpoint
                                }' must be for a host with a multicast address");
                        }
                        multicastInterface = value;

                        if (multicastInterface != "*" &&
                            IPAddress.TryParse(multicastInterface, out IPAddress? multicastInterfaceAddr))
                        {
                            if (ipAddress?.AddressFamily != multicastInterfaceAddr.AddressFamily)
                            {
                                throw new FormatException(
                                    $@"the address family of the interface in '{endpoint
                                    }' is not the multicast address family");
                            }

                            if (multicastInterfaceAddr == IPAddress.Any || multicastInterfaceAddr == IPAddress.IPv6Any)
                            {
                                multicastInterface = "*";
                            }
                        }
                        // else keep value such as eth0
                        break;

                    default:
                        throw new FormatException($"unknown local parameter '{name}' in endpoint '{endpoint}'");
                }
            }

            return (endpoint.ParseExternalUdpParams(), ttl, multicastInterface);
        }

        /// <summary>Parses the non-local parameters of endpoint.</summary>
        internal static bool ParseExternalUdpParams(this Endpoint endpoint)
        {
            if (endpoint.Protocol != Protocol.Ice1)
            {
                throw new FormatException($"endpoint '{endpoint}': protocol/transport mistmatch");
            }

            bool compress = false;

            foreach ((string name, string value) in endpoint.ExternalParams)
            {
                switch (name)
                {
                    case "-z":
                        if (compress)
                        {
                            throw new FormatException($"multiple -z parameters in endpoint '{endpoint}'");
                        }
                        if (value.Length > 0)
                        {
                            throw new FormatException($"invalid value '{value}' for parameter -z in endpoint '{endpoint}'");
                        }
                        compress = true;
                        break;

                    default:
                        throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
                }
            }
            return compress;
        }
    }
}
