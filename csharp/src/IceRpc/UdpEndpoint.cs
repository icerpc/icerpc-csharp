// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

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
                if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, IsIPv6Only);
                }
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);

                Network.SetBufSize(socket, Communicator, Transport.UDP);

                var addr = new IPEndPoint(Address, Port);
                IPEndPoint? multicastAddress = null;
                if (Network.IsMulticast(Address))
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

                if (Communicator.TransportLogger.IsEnabled(LogLevel.Debug))
                {
                    Communicator.TransportLogger.LogBindingSocketAttempt(Transport, Network.LocalAddrToString(addr));
                }

                socket.Bind(addr);

                ushort port = (ushort)((IPEndPoint)socket.LocalEndPoint!).Port;
                if (multicastAddress != null)
                {
                    multicastAddress.Port = port;
                    Network.SetMulticastGroup(socket, multicastAddress.Address, MulticastInterface);
                }

                var endpoint = Clone(port);
                var udpSocket = new UdpSocket(Communicator.TransportLogger, socket, multicastAddress);
                var multiStreamSocket = new Ice1NetworkSocket(udpSocket, endpoint, server);
                return new UdpConnection(endpoint, multiStreamSocket, label: null, server);
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

        protected internal override Connection CreateConnection(
            object? label,
            CancellationToken cancel)
        {
            EndPoint endpoint = HasDnsHost ? new DnsEndPoint(Host, Port) : new IPEndPoint(Address, Port);

            var socket = HasDnsHost ?
                new Socket(SocketType.Stream, ProtocolType.Tcp) :
                new Socket(Address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                if (endpoint is IPEndPoint ipEndpoint && Network.IsMulticast(ipEndpoint.Address))
                {
                    // IP multicast socket options require a socket created with the correct address family.
                    if (MulticastInterface != null)
                    {
                        Debug.Assert(MulticastInterface.Length > 0);
                        Network.SetMulticastInterface(socket, MulticastInterface, ipEndpoint.AddressFamily);
                    }
                    if (MulticastTtl != -1)
                    {
                        socket.Ttl = (short)MulticastTtl;
                    }
                }
                Network.SetBufSize(socket, Communicator, Transport.UDP);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex, RetryPolicy.NoRetry);
            }

            var udpSocket = new UdpSocket(Communicator.TransportLogger, socket, endpoint);
            return new UdpConnection(this, new Ice1NetworkSocket(udpSocket, this, server: null), label, server: null);
        }

        protected internal override void WriteOptions(OutputStream ostr)
        {
            Debug.Assert(Protocol == Protocol.Ice1);
            base.WriteOptions(ostr);
            ostr.WriteBool(_hasCompressionFlag);
        }

        internal static UdpEndpoint CreateIce1Endpoint(Transport transport, InputStream istr)
        {
            Debug.Assert(transport == Transport.UDP);
            return new UdpEndpoint(new EndpointData(transport,
                                                    host: istr.ReadString(),
                                                    port: ReadPort(istr),
                                                    Array.Empty<string>()),
                                   compress: istr.ReadBool(),
                                   istr.Communicator!);
        }

        internal static UdpEndpoint ParseIce1Endpoint(
            Transport transport,
            Dictionary<string, string?> options,
            Communicator communicator,
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
                else if (!IPAddress.TryParse(host, out IPAddress? address) || !Network.IsMulticast(address))
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
                                   options,
                                   communicator,
                                   serverEndpoint,
                                   endpointString);
        }

        // Constructor for ice1 unmarshaling
        private UdpEndpoint(EndpointData data, bool compress, Communicator communicator)
            : base(data, communicator, Protocol.Ice1) =>
            _hasCompressionFlag = compress;

        // Constructor for ice1 parsing
        private UdpEndpoint(
            EndpointData data,
            bool compress,
            int ttl,
            string? multicastInterface,
            Dictionary<string, string?> options,
            Communicator communicator,
            bool serverEndpoint,
            string endpointString)
            : base(data, options, communicator, serverEndpoint, endpointString)
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
