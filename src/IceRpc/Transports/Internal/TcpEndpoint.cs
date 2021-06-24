// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Endpoint class for the TCP transport.</summary>
    internal class TcpEndpoint : IPEndpoint, IClientConnectionFactory, IListenerFactory
    {
        public override bool IsDatagram => false;
        public override bool? IsSecure => Protocol == Protocol.Ice1 ? Transport == Transport.SSL : _tls;

        public override string? this[string option] =>
            option switch
            {
                "compress" => HasCompressionFlag ? "true" : null,
                "timeout" => Timeout != DefaultTimeout ?
                             Timeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) : null,
                "tls" => _tls?.ToString().ToLowerInvariant(),
                _ => base[option],
            };

        protected internal override bool HasOptions => Protocol == Protocol.Ice1 || _tls != null;

        public MultiStreamConnection CreateClientConnection(ClientConnectionOptions options, ILogger logger)
        {
            TcpOptions tcpOptions = options.TransportOptions as TcpOptions ?? TcpOptions.Default;
            EndPoint netEndPoint = HasDnsHost ? new DnsEndPoint(Host, Port) : new IPEndPoint(Address, Port);

            // We still specify the address family for the socket if an address is set to ensure an IPv4 socket is
            // created if the address is an IPv4 address.
            Socket socket = HasDnsHost ?
                new Socket(SocketType.Stream, ProtocolType.Tcp) :
                new Socket(Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !tcpOptions.IsIPv6Only;
                }

                if (tcpOptions.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    socket.Bind(localEndPoint);
                }

                SetBufferSize(socket, tcpOptions.ReceiveBufferSize, tcpOptions.SendBufferSize, logger);
                socket.NoDelay = true;
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            var tcpSocket = new TcpSocket(socket, logger, netEndPoint);
            return tcpSocket.CreateConnection(this, options);
        }

        public IListener CreateListener(ServerConnectionOptions options, ILogger logger)
        {
            if (Address == IPAddress.None)
            {
                throw new NotSupportedException(
                    $"endpoint '{this}' cannot accept connections because it has a DNS name");
            }

            var address = new IPEndPoint(Address, Port);
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                TcpOptions tcpOptions = options.TransportOptions as TcpOptions ?? TcpOptions.Default;
                if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !tcpOptions.IsIPv6Only;
                }

                socket.ExclusiveAddressUse = true;

                SetBufferSize(socket, tcpOptions.ReceiveBufferSize, tcpOptions.SendBufferSize, logger);

                socket.Bind(address);
                address = (IPEndPoint)socket.LocalEndPoint!;
                socket.Listen(tcpOptions.ListenerBackLog);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            return new TcpListener(socket, endpoint: Clone((ushort)address.Port), logger, options);
        }

        private protected bool HasCompressionFlag { get; }
        private protected TimeSpan Timeout { get; } = DefaultTimeout;

        /// <summary>The default timeout for ice1 endpoints.</summary>
        protected static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        /// <summary>The TLS option of this endpoint. Applies only to endpoints with the ice2 protocol.</summary>
        /// <value>True means use TLS, false means do no use TLS, and null means the TLS usage is to be determined.
        /// </value>
        private readonly bool? _tls;

        public override bool Equals(Endpoint? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (Protocol == Protocol.Ice1)
            {
                return other is TcpEndpoint tcpEndpoint &&
                    HasCompressionFlag == tcpEndpoint.HasCompressionFlag &&
                    Timeout == tcpEndpoint.Timeout &&
                    base.Equals(tcpEndpoint);
            }
            else
            {
                return other is TcpEndpoint tcpEndpoint && _tls == tcpEndpoint._tls && base.Equals(other);
            }
        }

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            base.AppendOptions(sb, optionSeparator);

            if (Protocol == Protocol.Ice1)
            {
                // InfiniteTimeSpan yields -1 and we use -1 instead of "infinite" for compatibility with Ice 3.5.
                sb.Append(" -t ");
                sb.Append(Timeout.TotalMilliseconds);

                if (HasCompressionFlag)
                {
                    sb.Append(" -z");
                }
            }
            else if (_tls is bool tls)
            {
                sb.Append($"tls={tls.ToString().ToLowerInvariant()}");
            }
        }

        // We ignore the Timeout and HasCompressionFlag properties when checking if two TCP endpoints are equivalent.
        protected internal override bool IsEquivalent(Endpoint? other) =>
            ReferenceEquals(this, other) ||
            (other is TcpEndpoint otherTcpEndpoint &&
                (_tls == otherTcpEndpoint._tls || _tls == null || otherTcpEndpoint._tls == null) && base.Equals(other));

        protected internal override void WriteOptions11(OutputStream ostr)
        {
            Debug.Assert(Protocol == Protocol.Ice1 && ostr.Encoding == Encoding.V11);
            base.WriteOptions11(ostr);
            ostr.WriteInt((int)Timeout.TotalMilliseconds);
            ostr.WriteBool(HasCompressionFlag);
        }

        // internal because it's used by some tests
        internal static TcpEndpoint CreateEndpoint(EndpointData data, Protocol protocol)
        {
            if (data.Options.Count > 0)
            {
                // Drop all options since we don't understand any.
                data = new EndpointData(data.Transport, data.Host, data.Port, ImmutableList<string>.Empty);
            }
            return new TcpEndpoint(data, protocol);
        }

        internal static ITransportDescriptor GetTransportDescriptor(Transport transport) =>
            transport switch
            {
                Transport.TCP => new TcpTransportDescriptor(),
                Transport.SSL => new SslTransportDescriptor(),
                _ => throw new ArgumentException("transport must be either tcp or ssl", nameof(transport))
            };

        private protected static TimeSpan ParseTimeout(Dictionary<string, string?> options, string endpointString)
        {
            TimeSpan timeout = DefaultTimeout;

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
                        throw new FormatException($"invalid timeout value '{argument}' in endpoint '{endpointString}'");
                    }
                }
                options.Remove("-t");
            }
            return timeout;
        }

        // Constructor for ice1 unmarshaling and parsing
        private protected TcpEndpoint(EndpointData data, TimeSpan timeout, bool compress)
            : base(data, Protocol.Ice1)
        {
            Timeout = timeout;
            HasCompressionFlag = compress;
        }

        // Constructor for unmarshaling with the 2.0 encoding.
        private protected TcpEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
        }

        // Constructor for ice2 parsing.
        private protected TcpEndpoint(EndpointData data, bool? tls)
            : base(data, Protocol.Ice2) =>
            _tls = tls;

        // Clone constructor
        private protected TcpEndpoint(TcpEndpoint endpoint, string host, ushort port)
            : base(endpoint, host, port)
        {
            HasCompressionFlag = endpoint.HasCompressionFlag;
            Timeout = endpoint.Timeout;
            _tls = endpoint._tls;
        }

        private protected override IPEndpoint Clone(string host, ushort port) =>
            new TcpEndpoint(this, host, port);

        private static TcpEndpoint CreateIce1Endpoint(Transport transport, InputStream istr)
        {
            Debug.Assert(transport == Transport.TCP || transport == Transport.SSL);

            // This is correct in C# since arguments are evaluated left-to-right. This would not be correct in C++ where
            // the order of evaluation of function arguments is undefined.
            return new TcpEndpoint(new EndpointData(transport,
                                                    host: istr.ReadString(),
                                                    port: ReadPort(istr),
                                                    ImmutableList<string>.Empty),
                                   timeout: TimeSpan.FromMilliseconds(istr.ReadInt()),
                                   compress: istr.ReadBool());
        }

        private static TcpEndpoint CreateIce1Endpoint(
            Transport transport,
            Dictionary<string, string?> options,
            string endpointString)
        {
            Debug.Assert(transport == Transport.TCP || transport == Transport.SSL);
            (string host, ushort port) = ParseHostAndPort(options, endpointString);
            return new TcpEndpoint(new EndpointData(transport, host, port, ImmutableList<string>.Empty),
                                   ParseTimeout(options, endpointString),
                                   ParseCompress(options, endpointString));
        }

        private class TcpTransportDescriptor : IIce1TransportDescriptor, IIce2TransportDescriptor
        {
            public ushort DefaultUriPort => IPEndpoint.DefaultUriPort;

            public string Name => "tcp";

            public Transport Transport => Transport.TCP;

            public Endpoint CreateEndpoint(EndpointData endpointData, Protocol protocol) =>
                TcpEndpoint.CreateEndpoint(endpointData, protocol);

            public Endpoint CreateEndpoint(InputStream istr) => CreateIce1Endpoint(Transport, istr);

            public Endpoint CreateEndpoint(Dictionary<string, string?> options, string endpointString) =>
                CreateIce1Endpoint(Transport, options, endpointString);

            public Endpoint CreateEndpoint(string host, ushort port, Dictionary<string, string> options)
            {
                bool? tls = null;
                if (options.TryGetValue("tls", out string? value))
                {
                    tls = bool.Parse(value);
                    options.Remove("tls");
                }
                return new TcpEndpoint(new EndpointData(Transport.TCP, host, port, ImmutableList<string>.Empty), tls);
            }
        }

        private class SslTransportDescriptor : IIce1TransportDescriptor
        {
            public string Name => "ssl";

            public Transport Transport => Transport.SSL;

            public Endpoint CreateEndpoint(EndpointData endpointData, Protocol protocol) =>
                TcpEndpoint.CreateEndpoint(endpointData, protocol);

            public Endpoint CreateEndpoint(InputStream istr) => CreateIce1Endpoint(Transport, istr);

            public Endpoint CreateEndpoint(Dictionary<string, string?> options, string endpointString) =>
                CreateIce1Endpoint(Transport, options, endpointString);
        }
    }
}
