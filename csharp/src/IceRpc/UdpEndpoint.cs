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
                    socket.DualMode = !(OperatingSystem.IsMacOS() || udpOptions.IsIPv6Only);
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
                        // Windows does not allow binding to the multicast address itself so we bind to the wildcard
                        // instead. As a result, bidirectional connection won't work because the source address won't
                        // be the multicast address and the client will therefore reject the datagram.
                        addr = new IPEndPoint(
                            addr.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any,
                             addr.Port);
                    }
                }

                socket.Bind(addr);

                ushort port = (ushort)((IPEndPoint)socket.LocalEndPoint!).Port;

                if (multicastAddress != null)
                {
                    multicastAddress.Port = port;
                    SetMulticastGroup(socket, multicastAddress.Address);
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
                new Socket(SocketType.Dgram, ProtocolType.Udp) :
                new Socket(Address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                UdpOptions udpOptions = options.TransportOptions as UdpOptions ?? UdpOptions.Default;
                if (endpoint is IPEndPoint ipEndpoint && IsMulticast(ipEndpoint.Address))
                {
                    if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        socket.DualMode = !udpOptions.IsIPv6Only;
                    }

                    // IP multicast socket options require a socket created with the correct address family.
                    if (MulticastInterface != null)
                    {
                        Debug.Assert(MulticastInterface.Length > 0);
                        if (Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.IP,
                                SocketOptionName.MulticastInterface,
                                GetIPv4InterfaceAddress(MulticastInterface).GetAddressBytes());
                        }
                        else
                        {
                            socket.SetSocketOption(
                                SocketOptionLevel.IPv6,
                                SocketOptionName.MulticastInterface,
                                GetIPv6InterfaceIndex(MulticastInterface));
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

                if (multicastInterface == "*")
                {
                    if (!serverEndpoint)
                    {
                        throw new FormatException($"`--interface *' not valid for proxy endpoint `{endpointString}'");
                    }

                    // The MulticastInterface property is null for server endpoints with a wildcard interface address.
                    multicastInterface = null;
                }
                else if (!IPAddress.TryParse(host, out IPAddress? address) || !IsMulticast(address))
                {
                    throw new FormatException(
                        $@"`--interface' option is only valid for proxy endpoint using a multicast address `{
                        endpointString}'");
                }
                else if (IPAddress.TryParse(multicastInterface, out IPAddress? multicastInterfaceAddr))
                {
                    if (address?.AddressFamily != multicastInterfaceAddr.AddressFamily)
                    {
                        throw new FormatException(
                            $@"`--interface' option address family is different from the multicast address family `{
                            endpointString}'");
                    }

                    // The MulticastInterface property is null for server endpoints with a wildcard interface address.
                    if (multicastInterfaceAddr == IPAddress.Any)
                    {
                        if (!serverEndpoint)
                        {
                            throw new FormatException(
                                $"`--interface 0.0.0.0' is not valid for proxy endpoint `{endpointString}'");
                        }
                        multicastInterface = null;
                    }
                    else if(multicastInterfaceAddr == IPAddress.IPv6Any)
                    {
                        if (!serverEndpoint)
                        {
                            throw new FormatException(
                                $"`--interface \"::0\" is not valid for proxy endpoint `{endpointString}'");
                        }
                        multicastInterface = null;
                    }
                }
                options.Remove("--interface");
            }

            return new UdpEndpoint(new EndpointData(transport, host, port, Array.Empty<string>()),
                                   ParseCompress(options, endpointString),
                                   ttl,
                                   multicastInterface,
                                   serverEndpoint);
        }

        private static IPAddress GetIPv4InterfaceAddress(string iface)
        {
            // The iface parameter must either be an IP address, an index or the name of an interface. If it's an index
            // we just return it. If it's an IP address we search for an interface which has this IP address. If it's a
            // name we search an interface with this name.

            if (IPAddress.TryParse(iface, out IPAddress? address))
            {
                return address;
            }

            bool isIndex = int.TryParse(iface, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index);
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties ipProps = networkInterface.GetIPProperties();
                IPv4InterfaceProperties ipv4Props = ipProps.GetIPv4Properties();
                if (ipv4Props != null && isIndex ? ipv4Props.Index == index : networkInterface.Name == iface)
                {
                    foreach (UnicastIPAddressInformation unicastAddress in ipProps.UnicastAddresses)
                    {
                        Debug.Assert(unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork);
                        return unicastAddress.Address;
                    }
                }
            }

            throw new ArgumentException($"couldn't find interface `{iface}'");
        }

        private static int GetIPv6InterfaceIndex(string iface)
        {
            // The iface parameter must either be an IP address, an index or the name of an interface. If it's an index
            // we just return it. If it's an IP address we search for an interface which has this IP address. If it's a
            // name we search an interface with this name.
            if (int.TryParse(iface, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                return index;
            }

            bool isAddress = IPAddress.TryParse(iface, out IPAddress? address);
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties ipProps = networkInterface.GetIPProperties();
                IPv6InterfaceProperties ipv6Props = ipProps.GetIPv6Properties();
                if (ipv6Props != null)
                {
                    foreach (UnicastIPAddressInformation unicastAddress in ipProps.UnicastAddresses)
                    {
                        if (isAddress ? unicastAddress.Address.Equals(address) : networkInterface.Name == iface)
                        {
                            return ipv6Props.Index;
                        }
                    }
                }
            }

            throw new ArgumentException("couldn't find interface `" + iface + "'");
        }

        private static bool IsMulticast(IPAddress addr) =>
            addr.AddressFamily == AddressFamily.InterNetwork ?
                (addr.GetAddressBytes()[0] & 0xF0) == 0xE0 : addr.IsIPv6Multicast;

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

        private void SetMulticastGroup(Socket socket, IPAddress group)
        {
            if (MulticastInterface == null) // Wildcard
            {
                // Get all the interfaces that support multicast and add each interface to the multicast group.
                var indexes = new HashSet<int>();
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        networkInterface.SupportsMulticast)
                    {
                        IPInterfaceProperties ipProps = networkInterface.GetIPProperties();
                        ;
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
                        new MulticastOption(group, GetIPv4InterfaceAddress(MulticastInterface)));
                }
                else
                {
                    socket.SetSocketOption(
                        SocketOptionLevel.IPv6,
                        SocketOptionName.AddMembership,
                        new IPv6MulticastOption(group, GetIPv6InterfaceIndex(MulticastInterface)));
                }
            }
        }

        private protected override IPEndpoint Clone(string host, ushort port) =>
            new UdpEndpoint(this, host, port);
    }
}
