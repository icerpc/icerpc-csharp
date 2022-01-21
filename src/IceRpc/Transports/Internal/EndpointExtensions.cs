// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Globalization;
using System.Net;

namespace IceRpc.Transports.Internal
{
    /// <summary>Extension methods for class Endpoint.</summary>
    internal static class EndpointExtensions
    {
        internal const int DefaultTcpTimeout = 60_000; // 60s

        internal static (TransportCode TransportCode, byte EncodingMajor, byte EncodingMinor, ReadOnlyMemory<byte> Bytes) ParseOpaqueParams(
           this Endpoint endpoint)
        {
            TransportCode? transportCode = null;
            ReadOnlyMemory<byte> bytes = default;
            byte encodingMajor = 1;
            byte encodingMinor = 1;

            foreach ((string name, string value) in endpoint.Params)
            {
                switch (name)
                {
                    case "transport":
                        if (value != TransportNames.Opaque)
                        {
                            throw new FormatException(
                                $"invalid value for transport parameter in endpoint '{endpoint}'");
                        }
                        break;

                    case "e":
                        (encodingMajor, encodingMinor) = value switch
                        {
                            "1.0" => ((byte)1, (byte)0),
                            "1.1" => ((byte)1, (byte)1),
                            _ => throw new FormatException($"invalid value for e parameter in endpoint '{endpoint}'")
                        };
                        break;

                    case "t":
                        short t;
                        try
                        {
                            t = short.Parse(value, CultureInfo.InvariantCulture);
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException(
                                $"invalid value for parameter t in endpoint '{endpoint}'", ex);
                        }

                        if (t < 0)
                        {
                            throw new FormatException(
                                $"value for t parameter out of range in endpoint '{endpoint}'");
                        }

                        transportCode = (TransportCode)t;
                        break;

                    case "v":
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
                throw new FormatException($"missing t parameter in endpoint '{endpoint}'");
            }
            else if (bytes.Length == 0)
            {
                throw new FormatException($"missing v parameter in endpoint '{endpoint}'");
            }

            return (transportCode.Value, encodingMajor, encodingMinor, bytes);
        }

        internal static (bool Compress, int Timeout, bool? Tls) ParseTcpParams(this Endpoint endpoint)
        {
            bool? tls = null;
            bool compress = false;
            int? timeout = null;

            foreach ((string name, string value) in endpoint.Params)
            {
                switch (name)
                {
                    case "transport":
                        if (value != TransportNames.Tcp)
                        {
                            throw new FormatException(
                                $"invalid value for transport parameter in endpoint '{endpoint}'");
                        }
                        break;

                    case "t":
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
                                    $"invalid value for t parameter in endpoint '{endpoint}'");
                            }
                        }
                        break;

                    case "tls":
                        try
                        {
                            tls = bool.Parse(value);
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException($"invalid value for tls parameter in endpoint '{endpoint}'", ex);
                        }
                        break;

                    case "z":
                        if (value.Length > 0)
                        {
                            throw new FormatException(
                                $"invalid value '{value}' for parameter z in endpoint '{endpoint}'");
                        }
                        compress = true;
                        break;

                    default:
                        throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
                }
            }

            return (compress, timeout ?? DefaultTcpTimeout, tls);
        }

        internal static (bool Compress, int Ttl, string? MulticastInterface) ParseUdpParams(this Endpoint endpoint)
        {
            bool compress = false;
            int ttl = -1;
            string? multicastInterface = null;

            foreach ((string name, string value) in endpoint.Params)
            {
                switch (name)
                {
                    case "transport":
                        if (value != TransportNames.Udp)
                        {
                            throw new FormatException(
                                $"invalid value for transport parameter in endpoint '{endpoint}'");
                        }
                        break;

                    case "z":
                        if (value.Length > 0)
                        {
                            throw new FormatException(
                                $"invalid value '{value}' for parameter z in endpoint '{endpoint}'");
                        }
                        compress = true;
                        break;

                    case "ttl":
                        if (value.Length == 0)
                        {
                            throw new FormatException(
                                $"no value provided for ttl parameter in endpoint '{endpoint}'");
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

                    case "interface":
                        if (value.Length == 0)
                        {
                            throw new FormatException(
                                $"no value provided for interface parameter in endpoint '{endpoint}'");
                        }
                        if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress) ||
                            !UdpUtils.IsMulticast(ipAddress))
                        {
                            throw new FormatException(@$"interface parameter in endpoint '{endpoint
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

            return (compress, ttl, multicastInterface);
        }

        /// <summary>Adds the transport parameter to this endpoint if null, and does nothing if it's already set to the
        /// correct value.</summary>
        /// <exception cref="ArgumentException">Thrown if endpoint already holds another transport.</exception>
        internal static Endpoint WithTransport(this Endpoint endpoint, string transport)
        {
            if (endpoint.Params.TryGetValue("transport", out string? endpointTransport))
            {
                if (endpointTransport != transport)
                {
                    throw new ArgumentException(
                        $"cannot use {transport} transport with endpoint '{endpoint}'",
                        nameof(endpoint));
                }
            }
            else
            {
                endpoint = endpoint with
                {
                    Params = endpoint.Params.Add("transport", transport)
                };
            }

            return endpoint;
        }
    }
}
