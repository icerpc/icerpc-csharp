// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal static class UdpUtils
    {
        internal static IPAddress GetIPv4InterfaceAddress(string @interface)
        {
            // The @interface parameter must either be an IP address, an index or the name of an interface. If it's an
            // index we just return it. If it's an IP address we search for an interface which has this IP address. If
            // it's a name we search an interface with this name.

            if (IPAddress.TryParse(@interface, out IPAddress? address))
            {
                return address;
            }

            bool isIndex = int.TryParse(@interface, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index);
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties ipProps = networkInterface.GetIPProperties();
                IPv4InterfaceProperties ipv4Props = ipProps.GetIPv4Properties();
                if (ipv4Props != null && isIndex ? ipv4Props.Index == index : networkInterface.Name == @interface)
                {
                    foreach (UnicastIPAddressInformation unicastAddress in ipProps.UnicastAddresses)
                    {
                        Debug.Assert(unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork);
                        return unicastAddress.Address;
                    }
                }
            }

            throw new ArgumentException($"could not find interface '{@interface}'", nameof(@interface));
        }

        internal static int GetIPv6InterfaceIndex(string @interface)
        {
            // The @interface parameter must either be an IP address, an index or the name of an interface. If it's an
            // index we just return it. If it's an IP address we search for an interface which has this IP address. If
            // it's a name we search an interface with this name.
            if (int.TryParse(@interface, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                return index;
            }

            bool isAddress = IPAddress.TryParse(@interface, out IPAddress? address);
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties ipProps = networkInterface.GetIPProperties();
                IPv6InterfaceProperties ipv6Props = ipProps.GetIPv6Properties();
                if (ipv6Props != null)
                {
                    foreach (UnicastIPAddressInformation unicastAddress in ipProps.UnicastAddresses)
                    {
                        if (isAddress ? unicastAddress.Address.Equals(address) : networkInterface.Name == @interface)
                        {
                            return ipv6Props.Index;
                        }
                    }
                }
            }

            throw new ArgumentException($"could not find interface '{@interface}'", nameof(@interface));
        }

        internal static bool IsMulticast(IPAddress addr) =>
            addr.AddressFamily == AddressFamily.InterNetwork ?
                (addr.GetAddressBytes()[0] & 0xF0) == 0xE0 : addr.IsIPv6Multicast;

        internal static (bool Compress, int Ttl, string? MulticastInterface) ParseUdpParams(Endpoint endpoint)
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
                        if (!IPAddress.TryParse(endpoint.Host, out IPAddress? ipAddress) || !IsMulticast(ipAddress))
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

            return (ParseExternalUdpParams(endpoint), ttl, multicastInterface);
        }

        /// <summary>Parses the non-local parameters of endpoint.</summary>
        internal static bool ParseExternalUdpParams(Endpoint endpoint)
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

        internal static void SetMulticastGroup(Socket socket, string? multicastInterface, IPAddress group)
        {
            if (multicastInterface == null || multicastInterface == "*")
            {
                // Get all the interfaces that support multicast and add each interface to the multicast group.
                var indexes = new HashSet<int>();
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        networkInterface.SupportsMulticast)
                    {
                        IPInterfaceProperties ipProps = networkInterface.GetIPProperties();

                        if (ipProps.UnicastAddresses.Select(addr => addr.Address).FirstOrDefault(
                                addr => addr.AddressFamily == group.AddressFamily) is IPAddress address)
                        {
                            if (group.AddressFamily == AddressFamily.InterNetwork)
                            {
                                socket.SetSocketOption(
                                    SocketOptionLevel.IP,
                                    SocketOptionName.AddMembership,
                                    new MulticastOption(group, address));
                            }
                            else
                            {
                                int index = GetIPv6InterfaceIndex(address.ToString());
                                if (!indexes.Contains(index))
                                {
                                    indexes.Add(index);
                                    socket.SetSocketOption(
                                        SocketOptionLevel.IPv6,
                                        SocketOptionName.AddMembership,
                                        new IPv6MulticastOption(group, index));
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (group.AddressFamily == AddressFamily.InterNetwork)
                {
                    socket.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.AddMembership,
                        new MulticastOption(group, GetIPv4InterfaceAddress(multicastInterface)));
                }
                else
                {
                    socket.SetSocketOption(
                        SocketOptionLevel.IPv6,
                        SocketOptionName.AddMembership,
                        new IPv6MulticastOption(group, GetIPv6InterfaceIndex(multicastInterface)));
                }
            }
        }
    }
}
