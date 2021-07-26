// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Endpoint class for the UDP transport.</summary>
    internal sealed class UdpEndpoint : IPEndpoint
    {
        public override bool IsDatagram => true;
        public override bool? IsSecure => false;

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

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            Debug.Assert(Protocol == Protocol.Ice1);

            base.AppendOptions(sb, optionSeparator);

            if (MulticastInterface != null)
            {
                Debug.Assert(MulticastInterface.Length > 0);
                bool addQuote = MulticastInterface.IndexOf(':', StringComparison.InvariantCulture) != -1;
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

        protected internal override void EncodeOptions11(IceEncoder encoder)
        {
            Debug.Assert(Protocol == Protocol.Ice1 && encoder.Encoding == Encoding.V11);
            base.EncodeOptions11(encoder);
            encoder.EncodeBool(_hasCompressionFlag);
        }

        internal static bool IsMulticast(IPAddress addr) =>
            addr.AddressFamily == AddressFamily.InterNetwork ?
                (addr.GetAddressBytes()[0] & 0xF0) == 0xE0 : addr.IsIPv6Multicast;

        // Constructor for unmarshaling
        internal UdpEndpoint(EndpointData data, bool compress = false)
            : base(data, Protocol.Ice1) =>
            _hasCompressionFlag = compress;

        // Constructor for ice1 parsing
        internal UdpEndpoint(EndpointData data, bool compress, int ttl, string? multicastInterface)
            : base(data, Protocol.Ice1)
        {
            _hasCompressionFlag = compress;
            MulticastTtl = ttl;
            MulticastInterface = multicastInterface;
        }

        internal UdpEndpoint Clone(EndPoint address)
        {
            if (address is IPEndPoint ipAddress)
            {
                string host = ipAddress.Address.ToString();
                ushort port = (ushort)ipAddress.Port;

                return (Host == host && Port == port) ? this : new UdpEndpoint(this, host, port);
            }
            else
            {
                throw new InvalidOperationException("unsupported address");
            }
        }

        private static IPAddress GetIPv4InterfaceAddress(string @interface)
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

        private static int GetIPv6InterfaceIndex(string @interface)
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

        // Clone constructor
        private UdpEndpoint(UdpEndpoint endpoint, string host, ushort port)
            : base(endpoint, host, port)
        {
            MulticastInterface = endpoint.MulticastInterface;
            MulticastTtl = endpoint.MulticastTtl;
            _hasCompressionFlag = endpoint._hasCompressionFlag;
        }

        private UdpEndpoint Clone(string hostName) =>
            hostName == Host ? this : new UdpEndpoint(this, hostName, Port);

        private UdpEndpoint Clone(ushort port) => port == Port ? this : new UdpEndpoint(this, Host, port);
    }

    internal class UdpEndpointFactory : IIce1EndpointFactory
    {
        public string Name => "udp";

        public TransportCode TransportCode => TransportCode.UDP;

        public Endpoint CreateEndpoint(EndpointData data, Protocol protocol)
        {
            if (protocol != Protocol.Ice1)
            {
                throw new ArgumentException($"cannot create UDP endpoint for protocol {protocol.GetName()}",
                                            nameof(protocol));
            }

            if (data.Options.Count > 0)
            {
                // Drop all options since we don't understand any.
                data = new EndpointData(Protocol.Ice1, "udp", data.TransportCode, data.Host, data.Port, ImmutableList<string>.Empty);
            }
            return new UdpEndpoint(data);
        }

        public Endpoint CreateIce1Endpoint(IceDecoder decoder) =>
            // This is correct in C# since arguments are evaluated left-to-right.
            new UdpEndpoint(new EndpointData(Protocol.Ice1,
                                            "udp",
                                             TransportCode,
                                             host: decoder.DecodeString(),
                                             port: checked((ushort)decoder.DecodeInt()),
                                             ImmutableList<string>.Empty),
                            compress: decoder.DecodeBool());

        public Endpoint CreateIce1Endpoint(Dictionary<string, string?> options, string endpointString)
        {
            (string host, ushort port) = Ice1Parser.ParseHostAndPort(options, endpointString);

            int ttl = -1;

            if (options.TryGetValue("--ttl", out string? argument))
            {
                if (argument == null)
                {
                    throw new FormatException(
                        $"no argument provided for --ttl option in endpoint '{endpointString}'");
                }
                try
                {
                    ttl = int.Parse(argument, CultureInfo.InvariantCulture);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"invalid TTL value '{argument}' in endpoint '{endpointString}'", ex);
                }

                if (ttl < 0)
                {
                    throw new FormatException(
                        $"TTL value '{argument}' out of range in endpoint '{endpointString}'");
                }
                options.Remove("--ttl");
            }

            string? multicastInterface = null;

            if (options.TryGetValue("--interface", out argument))
            {
                multicastInterface = argument ?? throw new FormatException(
                    $"no argument provided for --interface option in endpoint '{endpointString}'");

                if (!IPAddress.TryParse(host, out IPAddress? address) || !UdpEndpoint.IsMulticast(address))
                {
                    throw new FormatException(@$"--interface option in endpoint '{endpointString
                        }' must be for a host with a multicast address");
                }

                if (multicastInterface != "*" &&
                    IPAddress.TryParse(multicastInterface, out IPAddress? multicastInterfaceAddr))
                {
                    if (address?.AddressFamily != multicastInterfaceAddr.AddressFamily)
                    {
                        throw new FormatException(
                            $@"the address family of the interface in '{endpointString
                            }' is not the multicast address family");
                    }

                    if (multicastInterfaceAddr == IPAddress.Any || multicastInterfaceAddr == IPAddress.IPv6Any)
                    {
                        multicastInterface = "*";
                    }
                }
                // else keep argument such as eth0

                options.Remove("--interface");
            }

            return new UdpEndpoint(new EndpointData(Protocol.Ice1, "udp", TransportCode.UDP, host, port, ImmutableList<string>.Empty),
                                   Ice1Parser.ParseCompress(options, endpointString),
                                   ttl,
                                   multicastInterface);
        }
    }
}
