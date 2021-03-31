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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The Endpoint class for the UDP transport.</summary>
    internal sealed class UdpEndpoint : IPEndpoint
    {
        public override bool IsDatagram => true;

        public override string? this[string option] =>
            option switch
            {
                "interface" => MulticastInterface,
                "ttl" => MulticastTtl.ToString(CultureInfo.InvariantCulture),
                "compress" => _hasCompressionFlag ? "true" : null,
                _ => base[option],
            };

        /// <summary>The local network interface used to send multicast datagrams.</summary>
        internal string? MulticastInterface { get; }

        /// <summary>The time-to-live of the multicast datagrams, in hops.</summary>
        internal int MulticastTtl { get; } = -1;

        private readonly bool _hasCompressionFlag;

        private int _hashCode;

        public override IAcceptor Acceptor(Server server) =>
            throw new InvalidOperationException();

        public override Connection CreateDatagramServerConnection(Server server)
        {
            Debug.Assert(Address != IPAddress.None); // i.e. not a DNS name

            var socket = new Socket(Address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                IncomingConnectionOptions options = server.ConnectionOptions;
                UdpOptions udpOptions = options.TransportOptions as UdpOptions ?? UdpOptions.Default;

                if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    // TODO: Don't enable DualMode sockets on macOS, https://github.com/dotnet/corefx/issues/31182
                    if (OperatingSystem.IsMacOS())
                    {
                        socket.DualMode = false;
                    }
                    else
                    {
                        socket.DualMode = !udpOptions.IsIPv6Only;
                    }
                }
                socket.ExclusiveAddressUse = true;

                SetBufferSize(socket,
                              udpOptions.ReceiveBufferSize,
                              udpOptions.SendBufferSize,
                              server.Logger);

                var addr = new IPEndPoint(Address, Port);
                IPEndPoint? multicastAddress = null;
                if (IsMulticast(Address))
                {
                    multicastAddress = addr;

                    socket.ExclusiveAddressUse = false;
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    if (OperatingSystem.IsWindows())
                    {
                        // Windows does not allow binding to the multicast address itself so we bind to INADDR_ANY
                        // instead. As a result, bidirectional connection won't work because the source address won't
                        // be the multicast address and the client will therefore reject the datagram.
                        if (addr.AddressFamily == AddressFamily.InterNetwork)
                        {
                            addr = new IPEndPoint(IPAddress.Any, addr.Port);
                        }
                        else
                        {
                            addr = new IPEndPoint(IPAddress.IPv6Any, addr.Port);
                        }
                    }
                }

                socket.Bind(addr);

                ushort port = (ushort)((IPEndPoint)socket.LocalEndPoint!).Port;
                if (multicastAddress != null)
                {
                    multicastAddress.Port = port;
                    SetMulticastGroup(socket, multicastAddress.Address, MulticastInterface);
                }

                Endpoint endpoint = Clone(port);
                var udpSocket = new UdpSocket(socket, server.Logger, isIncoming: true, multicastAddress);
                var multiStreamSocket = new Ice1NetworkSocket(endpoint, udpSocket, options);
                return new UdpConnection(endpoint, multiStreamSocket, options, server);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex, RetryPolicy.NoRetry);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public override bool Equals(Endpoint? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return other is UdpEndpoint udpEndpoint &&
                _hasCompressionFlag == udpEndpoint._hasCompressionFlag &&
                MulticastInterface == udpEndpoint.MulticastInterface &&
                MulticastTtl == udpEndpoint.MulticastTtl &&
                base.Equals(udpEndpoint);
        }

        public override int GetHashCode()
        {
            // This code is thread safe because reading/writing _hashCode (an int) is atomic.
            if (_hashCode != 0)
            {
                // Return cached value
                return _hashCode;
            }
            else
            {
                var hash = new HashCode();
                hash.Add(base.GetHashCode());
                hash.Add(_hasCompressionFlag);
                hash.Add(MulticastInterface);
                hash.Add(MulticastTtl);
                int hashCode = hash.ToHashCode();
                if (hashCode == 0) // 0 is not a valid value as it means "not initialized".
                {
                    hashCode = 1;
                }
                _hashCode = hashCode;
                return _hashCode;
            }
        }
        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            Debug.Assert(Protocol == Protocol.Ice1);

            base.AppendOptions(sb, optionSeparator);

            if (MulticastInterface != null)
            {
                Debug.Assert(MulticastInterface.Length > 0);
                bool addQuote = MulticastInterface.IndexOf(':') != -1;
                sb.Append(" --interface ");
                if (addQuote)
                {
                    sb.Append('"');
                }
                sb.Append(MulticastInterface);
                if (addQuote)
                {
                    sb.Append('"');
                }
            }

            if (MulticastTtl != -1)
            {
                sb.Append(" --ttl ");
                sb.Append(MulticastTtl.ToString(CultureInfo.InvariantCulture));
            }

            if (_hasCompressionFlag)
            {
                sb.Append(" -z");
            }
        }

        protected internal override async Task<Connection> ConnectAsync(
            OutgoingConnectionOptions options,
            ILogger logger,
            CancellationToken cancel)
        {
            EndPoint endpoint = HasDnsHost ? new DnsEndPoint(Host, Port) : new IPEndPoint(Address, Port);

            Socket socket = HasDnsHost ?
                new Socket(SocketType.Stream, ProtocolType.Tcp) :
                new Socket(Address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                UdpOptions udpOptions = options.TransportOptions as UdpOptions ?? UdpOptions.Default;
                if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !udpOptions.IsIPv6Only;
                }

                if (endpoint is IPEndPoint ipEndpoint && IsMulticast(ipEndpoint.Address))
                {
                    // IP multicast socket options require a socket created with the correct address family.
                    if (MulticastInterface != null)
                    {
                        Debug.Assert(MulticastInterface.Length > 0);

                        if (Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.IP,
                                SocketOptionName.MulticastInterface,
                                GetInterfaceAddress(MulticastInterface, Address.AddressFamily)!.GetAddressBytes());
                        }
                        else
                        {
                            socket.SetSocketOption(SocketOptionLevel.IPv6,
                                SocketOptionName.MulticastInterface,
                                GetInterfaceIndex(MulticastInterface, socket.AddressFamily));
                        }

                    }
                    if (MulticastTtl != -1)
                    {
                        socket.Ttl = (short)MulticastTtl;
                    }
                }

                if (udpOptions.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    socket.Bind(localEndPoint);
                }

                SetBufferSize(socket, udpOptions.ReceiveBufferSize, udpOptions.SendBufferSize, logger);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex, RetryPolicy.NoRetry);
            }

            var udpSocket = new UdpSocket(socket, logger, isIncoming: false, endpoint);
            var multiStreamSocket = new Ice1NetworkSocket(this, udpSocket, options);
            var connection = new UdpConnection(this, multiStreamSocket, options, server: null);
            await connection.ConnectAsync(null, cancel).ConfigureAwait(false);
            return connection;
        }

        protected internal override void WriteOptions11(OutputStream ostr)
        {
            Debug.Assert(Protocol == Protocol.Ice1 && ostr.Encoding == Encoding.V11);
            base.WriteOptions11(ostr);
            ostr.WriteBool(_hasCompressionFlag);
        }

        internal static UdpEndpoint CreateEndpoint(EndpointData data, Protocol protocol)
        {
            if (data.Options.Length > 0)
            {
                // Drop all options since we don't understand any.
                data = new EndpointData(data.Transport, data.Host, data.Port, Array.Empty<string>());
            }
            return new(data, protocol);
        }

        internal static UdpEndpoint CreateIce1Endpoint(Transport transport, InputStream istr)
        {
            Debug.Assert(transport == Transport.UDP);
            return new UdpEndpoint(new EndpointData(transport,
                                                    host: istr.ReadString(),
                                                    port: ReadPort(istr),
                                                    Array.Empty<string>()),
                                   compress: istr.ReadBool());
        }

        internal static UdpEndpoint ParseIce1Endpoint(
            Transport transport,
            Dictionary<string, string?> options,
            bool serverEndpoint,
            string endpointString)
        {
            Debug.Assert(transport == Transport.UDP);

            (string host, ushort port) = ParseHostAndPort(options, serverEndpoint, endpointString);

            int ttl = -1;

            if (options.TryGetValue("--ttl", out string? argument))
            {
                if (argument == null)
                {
                    throw new FormatException($"no argument provided for --ttl option in endpoint `{endpointString}'");
                }
                try
                {
                    ttl = int.Parse(argument, CultureInfo.InvariantCulture);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"invalid TTL value `{argument}' in endpoint `{endpointString}'", ex);
                }

                if (ttl < 0)
                {
                    throw new FormatException($"TTL value `{argument}' out of range in endpoint `{endpointString}'");
                }
                options.Remove("--ttl");
            }

            string? multicastInterface = null;

            if (options.TryGetValue("--interface", out argument))
            {
                multicastInterface = argument ?? throw new FormatException(
                    $"no argument provided for --interface option in endpoint `{endpointString}'");

                if (serverEndpoint)
                {
                    throw new FormatException($"invalid `--interface' option in server endpoint `{endpointString}'");
                }
                else if (multicastInterface == "*")
                {
                    throw new FormatException($"`--interface *' not valid for proxy endpoint `{endpointString}'");
                }
                else if (!IPAddress.TryParse(host, out IPAddress? address) || !IsMulticast(address))
                {
                    throw new FormatException(
                        $@"`--interface' option is only valid for proxy endpoint using a multicast address `{
                        endpointString}'");
                }
                options.Remove("--interface");
            }

            return new UdpEndpoint(new EndpointData(transport, host, port, Array.Empty<string>()),
                                   ParseCompress(options, endpointString),
                                   ttl,
                                   multicastInterface,
                                   serverEndpoint);
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

        private static List<string> GetInterfacesForMulticast(string? intf, AddressFamily family)
        {
            var interfaces = new List<string>();

            bool isWildcard = false;
            if (intf != null && IPAddress.TryParse(intf, out IPAddress? addr))
            {
                isWildcard = addr.Equals(family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any);
            }

            if (intf == null || isWildcard)
            {
                interfaces.AddRange(GetLocalAddresses(family, true, true).Select(i => i.ToString()));
            }

            if (intf != null && interfaces.Count == 0)
            {
                interfaces.Add(intf);
            }
            return interfaces;
        }

        private static bool IsMulticast(IPAddress addr) =>
            addr.AddressFamily == AddressFamily.InterNetwork ?
                (addr.GetAddressBytes()[0] & 0xF0) == 0xE0 : addr.IsIPv6Multicast;

        private static void SetMulticastGroup(Socket s, IPAddress group, string? iface)
        {
            var indexes = new HashSet<int>();
            foreach (string intf in GetInterfacesForMulticast(iface, group.AddressFamily))
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

        // Constructor for ice1 unmarshaling
        private UdpEndpoint(EndpointData data, bool compress)
            : base(data, Protocol.Ice1) =>
            _hasCompressionFlag = compress;

        // Constructor for unmarshaling with the 2.0 encoding.
        private UdpEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
        }

        // Constructor for ice1 parsing
        private UdpEndpoint(EndpointData data, bool compress, int ttl, string? multicastInterface, bool serverEndpoint)
            : base(data, serverEndpoint, Protocol.Ice1)
        {
            _hasCompressionFlag = compress;
            MulticastTtl = ttl;
            MulticastInterface = multicastInterface;
        }

        // Clone constructor
        private UdpEndpoint(UdpEndpoint endpoint, string host, ushort port)
            : base(endpoint, host, port)
        {
            MulticastInterface = endpoint.MulticastInterface;
            MulticastTtl = endpoint.MulticastTtl;
            _hasCompressionFlag = endpoint._hasCompressionFlag;
        }
        private protected override IPEndpoint Clone(string host, ushort port) =>
            new UdpEndpoint(this, host, port);
    }
}
