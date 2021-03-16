// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IceRpc
{
    internal static class Network
    {
        // Which versions of the Internet Protocol are enabled?
        internal const int EnableIPv4 = 0;
        internal const int EnableIPv6 = 1;
        internal const int EnableBoth = 2;

        internal static Socket CreateServerSocket(IPEndpoint endpoint, AddressFamily family)
        {
            Socket socket = CreateSocket(endpoint.IsDatagram, family);
            if (family == AddressFamily.InterNetworkV6)
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IPv6,
                                           SocketOptionName.IPv6Only,
                                           endpoint.IsIPv6Only);
                }
                catch
                {
                    socket.CloseNoThrow();
                    throw;
                }
            }
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            return socket;
        }

        internal static void SetMulticastInterface(Socket socket, string iface, AddressFamily family)
        {
            if (family == AddressFamily.InterNetwork)
            {
                socket.SetSocketOption(SocketOptionLevel.IP,
                                       SocketOptionName.MulticastInterface,
                                       GetInterfaceAddress(iface, family)!.GetAddressBytes());
            }
            else
            {
                socket.SetSocketOption(SocketOptionLevel.IPv6,
                                       SocketOptionName.MulticastInterface,
                                       GetInterfaceIndex(iface, family));
            }
        }

        internal static Socket CreateSocket(bool udp, AddressFamily? addressFamily)
        {
            try
            {
                if (udp)
                {
                    if (addressFamily is AddressFamily value)
                    {
                        return new Socket(value, SocketType.Dgram, ProtocolType.Udp);
                    }
                    else
                    {
                        return new Socket(SocketType.Dgram, ProtocolType.Udp);
                    }
                }
                else
                {
                    Socket socket;
                    if (addressFamily is AddressFamily value)
                    {
                        socket = new Socket(value, SocketType.Stream, ProtocolType.Tcp);
                    }
                    else
                    {
                        socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    }

                    try
                    {
                        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);
                    }
                    catch
                    {
                        socket.CloseNoThrow();
                        throw;
                    }
                    return socket;
                }
            }
            catch (SocketException ex)
            {
                throw new TransportException(ex, RetryPolicy.OtherReplica);
            }
        }

        internal static List<string> GetInterfacesForMulticast(string? intf, int ipVersion)
        {
            var interfaces = new List<string>();

            if (intf == null || IsWildcard(intf, ipVersion))
            {
                interfaces.AddRange(GetLocalAddresses(ipVersion, true, true, true).Select(i => i.ToString()));
            }

            if (intf != null && interfaces.Count == 0)
            {
                interfaces.Add(intf);
            }
            return interfaces;
        }

        internal static int GetIPVersion(IPAddress addr) =>
            addr.AddressFamily == AddressFamily.InterNetwork ? EnableIPv4 : EnableIPv6;

        internal static EndPoint? GetLocalAddress(Socket socket)
        {
            try
            {
                return socket.LocalEndPoint;
            }
            catch
            {
            }
            return null;
        }

        internal static IPAddress[] GetLocalAddresses(
            int ipVersion,
            bool includeLoopback,
            bool singleAddressPerInterface,
            bool supportMulticast)
        {
            var addresses = new HashSet<IPAddress>();
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface ni in nics)
                {
                    if (supportMulticast && !ni.SupportsMulticast)
                    {
                        continue;
                    }

                    if (ni.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    UnicastIPAddressInformationCollection uniColl = ipProps.UnicastAddresses;
                    foreach (UnicastIPAddressInformation uni in uniColl)
                    {
                        if ((uni.Address.AddressFamily == AddressFamily.InterNetwork && ipVersion != EnableIPv6) ||
                            (uni.Address.AddressFamily == AddressFamily.InterNetworkV6 && ipVersion != EnableIPv4))
                        {
                            if (includeLoopback || !IPAddress.IsLoopback(uni.Address))
                            {
                                addresses.Add(uni.Address);
                                if (singleAddressPerInterface)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new TransportException(
                    "error retrieving local network interface IP addresses",
                    ex,
                    RetryPolicy.NoRetry);
            }

            return addresses.ToArray();
        }

        internal static EndPoint? GetRemoteAddress(Socket socket)
        {
            try
            {
                return socket.RemoteEndPoint;
            }
            catch
            {
            }
            return null;
        }

        internal static bool IsMulticast(IPAddress addr) =>
            addr.AddressFamily == AddressFamily.InterNetwork ?
                (addr.GetAddressBytes()[0] & 0xF0) == 0xE0 : addr.IsIPv6Multicast;

        /// <summary>Check if an IPEndPoint is on the same host, we consider a peer endpoint is on the same host
        /// if its address matches any of the host local addresses.</summary>
        /// <param name="peer">The peer endpoint to check.</param>
        /// <returns><c>True</c> if the peer is on the same host otherwise <c>false</c>.</returns>
        internal static bool IsSameHost(this IPEndPoint peer)
        {
            try
            {
                return GetLocalAddresses(EnableBoth, true, false, false).Any(address => address.Equals(peer.Address));
            }
            catch
            {
            }
            return false;
        }

        internal static string LocalAddrToString(Socket socket) => LocalAddrToString(GetLocalAddress(socket));

        internal static string LocalAddrToString(EndPoint? endpoint) => endpoint?.ToString() ?? "<not bound>";

        internal static string RemoteAddrToString(Socket socket) => RemoteAddrToString(GetRemoteAddress(socket));

        internal static string RemoteAddrToString(EndPoint? endpoint) => endpoint?.ToString() ?? "<not connected>";

        internal static void SetBufSize(Socket socket, Communicator communicator, Transport transport)
        {
            int rcvSize = communicator.GetPropertyAsByteSize($"Ice.{transport}.RcvSize") ?? 0;
            if (rcvSize > 0)
            {
                // Try to set the buffer size. The kernel will silently adjust the size to an acceptable value. Then
                // read the size back to get the size that was actually set.
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, rcvSize);
                int size = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer)!;
                if (size < rcvSize)
                {
                    // Warn if the size that was set is less than the requested size and we have not already warned.
                    BufWarnSizeInfo warningInfo = communicator.GetBufWarnSize(Transport.TCP);
                    if ((!warningInfo.RcvWarn || rcvSize != warningInfo.RcvSize) &&
                        communicator.Logger.IsEnabled(LogLevel.Debug))
                    {
                        communicator.Logger.LogReceiveBufferSizeAdjusted(transport, rcvSize, size);
                        communicator.SetRcvBufWarnSize(Transport.TCP, rcvSize);
                    }
                }
            }

            int sndSize = communicator.GetPropertyAsByteSize($"Ice.{transport}.SndSize") ?? 0;
            if (sndSize > 0)
            {
                // Try to set the buffer size. The kernel will silently adjust the size to an acceptable value. Then
                // read the size back to get the size that was actually set.
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, sndSize);
                int size = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer)!;
                if (size < sndSize) // Warn if the size that was set is less than the requested size.
                {
                    // Warn if the size that was set is less than the requested size and we have not already warned.
                    BufWarnSizeInfo warningInfo = communicator.GetBufWarnSize(Transport.TCP);
                    if ((!warningInfo.SndWarn || sndSize != warningInfo.SndSize) &&
                        communicator.Logger.IsEnabled(LogLevel.Debug))
                    {
                        communicator.Logger.LogReceiveBufferSizeAdjusted(transport, sndSize, size);
                        communicator.SetSndBufWarnSize(Transport.TCP, sndSize);
                    }
                }
            }
        }

        internal static void SetMulticastGroup(Socket s, IPAddress group, string? iface)
        {
            var indexes = new HashSet<int>();
            foreach (string intf in GetInterfacesForMulticast(iface, GetIPVersion(group)))
            {
                if (group.AddressFamily == AddressFamily.InterNetwork)
                {
                    MulticastOption option;
                    IPAddress? addr = GetInterfaceAddress(intf, group.AddressFamily);
                    if (addr == null)
                    {
                        option = new MulticastOption(group);
                    }
                    else
                    {
                        option = new MulticastOption(group, addr);
                    }
                    s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, option);
                }
                else
                {
                    int index = GetInterfaceIndex(intf, group.AddressFamily);
                    if (!indexes.Contains(index))
                    {
                        indexes.Add(index);
                        IPv6MulticastOption option;
                        if (index == -1)
                        {
                            option = new IPv6MulticastOption(group);
                        }
                        else
                        {
                            option = new IPv6MulticastOption(group, index);
                        }
                        s.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, option);
                    }
                }
            }
        }

        internal static string SocketToString(Socket? socket)
        {
            try
            {
                if (socket == null)
                {
                    return "<closed>";
                }
                var s = new System.Text.StringBuilder();
                s.Append("local address = " + LocalAddrToString(GetLocalAddress(socket)));
                s.Append("\nremote address = " + RemoteAddrToString(GetRemoteAddress(socket)));
                return s.ToString();
            }
            catch (ObjectDisposedException)
            {
                return "<closed>";
            }
        }

        private static IPAddress? GetInterfaceAddress(string iface, AddressFamily family)
        {
            Debug.Assert(iface.Length > 0);

            // The iface parameter must either be an IP address, an index or the name of an interface. If it's an index
            // we just return it. If it's an IP address we search for an interface which has this IP address. If it's a
            // name we search an interface with this name.
            try
            {
                return IPAddress.Parse(iface);
            }
            catch (FormatException)
            {
            }

            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            try
            {
                int index = int.Parse(iface, CultureInfo.InvariantCulture);
                foreach (NetworkInterface ni in nics)
                {
                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    int interfaceIndex = -1;
                    if (family == AddressFamily.InterNetwork)
                    {
                        IPv4InterfaceProperties ipv4Props = ipProps.GetIPv4Properties();
                        if (ipv4Props != null && ipv4Props.Index == index)
                        {
                            interfaceIndex = ipv4Props.Index;
                        }
                    }
                    else
                    {
                        IPv6InterfaceProperties ipv6Props = ipProps.GetIPv6Properties();
                        if (ipv6Props != null && ipv6Props.Index == index)
                        {
                            interfaceIndex = ipv6Props.Index;
                        }
                    }
                    if (interfaceIndex >= 0)
                    {
                        foreach (UnicastIPAddressInformation a in ipProps.UnicastAddresses)
                        {
                            if (a.Address.AddressFamily == family)
                            {
                                return a.Address;
                            }
                        }
                    }
                }
            }
            catch (FormatException)
            {
            }

            foreach (NetworkInterface ni in nics)
            {
                if (ni.Name == iface)
                {
                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation a in ipProps.UnicastAddresses)
                    {
                        if (a.Address.AddressFamily == family)
                        {
                            return a.Address;
                        }
                    }
                }
            }

            throw new ArgumentException($"couldn't find interface `{iface}'");
        }

        private static int GetInterfaceIndex(string iface, AddressFamily family)
        {
            if (iface.Length == 0)
            {
                return -1;
            }

            // The iface parameter must either be an IP address, an index or the name of an interface. If it's an index
            // we just return it. If it's an IP address we search for an interface which has this IP address. If it's a
            // name we search an interface with this name.
            try
            {
                return int.Parse(iface, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
            }

            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            try
            {
                var addr = IPAddress.Parse(iface);
                foreach (NetworkInterface ni in nics)
                {
                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation uni in ipProps.UnicastAddresses)
                    {
                        if (uni.Address.Equals(addr))
                        {
                            if (addr.AddressFamily == AddressFamily.InterNetwork)
                            {
                                IPv4InterfaceProperties ipv4Props = ipProps.GetIPv4Properties();
                                if (ipv4Props != null)
                                {
                                    return ipv4Props.Index;
                                }
                            }
                            else
                            {
                                IPv6InterfaceProperties ipv6Props = ipProps.GetIPv6Properties();
                                if (ipv6Props != null)
                                {
                                    return ipv6Props.Index;
                                }
                            }
                        }
                    }
                }
            }
            catch (FormatException)
            {
            }

            foreach (NetworkInterface ni in nics)
            {
                if (ni.Name == iface)
                {
                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    if (family == AddressFamily.InterNetwork)
                    {
                        IPv4InterfaceProperties ipv4Props = ipProps.GetIPv4Properties();
                        if (ipv4Props != null)
                        {
                            return ipv4Props.Index;
                        }
                    }
                    else
                    {
                        IPv6InterfaceProperties ipv6Props = ipProps.GetIPv6Properties();
                        if (ipv6Props != null)
                        {
                            return ipv6Props.Index;
                        }
                    }
                }
            }

            throw new ArgumentException("couldn't find interface `" + iface + "'");
        }

        private static bool IsWildcard(string address, int ipVersion)
        {
            Debug.Assert(address.Length > 0);

            try
            {
                var addr = IPAddress.Parse(address);
                return ipVersion != EnableIPv4 ? addr.Equals(IPAddress.IPv6Any) : addr.Equals(IPAddress.Any);
            }
            catch
            {
            }

            return false;
        }
    }
}
