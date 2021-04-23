// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The Endpoint class for the TCP transport.</summary>
    internal class TcpEndpoint : IPEndpoint
    {
        public override bool IsDatagram => false;
        public override bool IsAlwaysSecure => Transport == Transport.SSL;

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

        private protected bool HasCompressionFlag { get; }
        private protected TimeSpan Timeout { get; } = DefaultTimeout;

        /// <summary>The default timeout for ice1 endpoints.</summary>
        protected static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        /// <summary>The TLS option if this endpoint. Applies only to endpoints with the ice2 protocol.</summary>
        /// <value>True means use TLS, false means do no use TLS, and null means the TLS usage is to be determined.
        /// </value>
        private readonly bool? _tls;

        // TODO: should not be public
        public override IAcceptor Acceptor(Server server)
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
                TcpOptions tcpOptions = server.ConnectionOptions.TransportOptions as TcpOptions ?? TcpOptions.Default;
                if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !tcpOptions.IsIPv6Only;
                }

                socket.ExclusiveAddressUse = true;

                SetBufferSize(socket, tcpOptions.ReceiveBufferSize, tcpOptions.SendBufferSize, server.Logger);

                socket.Bind(address);
                address = (IPEndPoint)socket.LocalEndPoint!;
                socket.Listen(tcpOptions.ListenerBackLog);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }
            return new TcpAcceptor(socket, (TcpEndpoint)Clone((ushort)address.Port), server);
        }

        public override Connection CreateDatagramServerConnection(Server server) =>
            throw new InvalidOperationException();

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
                if (base.HasOptions)
                {
                    sb.Append(optionSeparator);
                }
                sb.Append($"tls={tls.ToString().ToLowerInvariant()}");
            }
        }

        // We ignore the Timeout and HasCompressionFlag properties when checking if two TCP endpoints are equivalent.
        // We also ignore _tls: if we already have a tls=false connection established, we want to reuse it, even when
        // tls=true.
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

        internal static TcpEndpoint CreateIce1Endpoint(Transport transport, InputStream istr)
        {
            Debug.Assert(transport == Transport.TCP || transport == Transport.SSL);

            // This is correct in C# since arguments are evaluated left-to-right. This would not be correct in C++ where
            // the order of evaluation of function arguments is undefined.
            return new TcpEndpoint(new EndpointData(transport,
                                                    host: istr.ReadString(),
                                                    port: ReadPort(istr),
                                                    Array.Empty<string>()),
                                   timeout: TimeSpan.FromMilliseconds(istr.ReadInt()),
                                   compress: istr.ReadBool());
        }

        internal static TcpEndpoint CreateEndpoint(EndpointData data, Protocol protocol)
        {
            if (data.Options.Length > 0)
            {
                // Drop all options since we don't understand any.
                data = new EndpointData(data.Transport, data.Host, data.Port, Array.Empty<string>());
            }
            return new(data, protocol);
        }

        internal static TcpEndpoint ParseIce1Endpoint(
            Transport transport,
            Dictionary<string, string?> options,
            string endpointString)
        {
            Debug.Assert(transport == Transport.TCP || transport == Transport.SSL);
            (string host, ushort port) = ParseHostAndPort(options, endpointString);
            return new TcpEndpoint(new EndpointData(transport, host, port, Array.Empty<string>()),
                                   ParseTimeout(options, endpointString),
                                   ParseCompress(options, endpointString));
        }

        internal static TcpEndpoint ParseIce2Endpoint(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> options)
        {
            Debug.Assert(transport == Transport.TCP);

            bool? tls = null;
            if (options.TryGetValue("tls", out string? value))
            {
                tls = bool.Parse(value);
                options.Remove("tls");
            }
            return new TcpEndpoint(new EndpointData(transport, host, port, Array.Empty<string>()), tls);
        }

        protected internal override async Task<Connection> ConnectAsync(
            OutgoingConnectionOptions options,
            ILogger logger,
            CancellationToken cancel)
        {
            // If the endpoint is always secure or a secure connection is required, connect with the SSL client
            // authentication options.
            SslClientAuthenticationOptions? authenticationOptions = null;
            if (Protocol == Protocol.Ice1 ? IsAlwaysSecure : (_tls ?? true))
            {
                authenticationOptions = options.AuthenticationOptions ?? new SslClientAuthenticationOptions()
                {
                    TargetHost = Host
                };
            }

            TcpOptions tcpOptions = options.TransportOptions as TcpOptions ?? TcpOptions.Default;
            EndPoint endpoint = HasDnsHost ? new DnsEndPoint(Host, Port) : new IPEndPoint(Address, Port);
            SingleStreamSocket socket = CreateSocket(endpoint, tcpOptions, logger);
            MultiStreamOverSingleStreamSocket multiStreamSocket = Protocol switch
            {
                Protocol.Ice1 => new Ice1NetworkSocket(this, socket, options),
                _ => new SlicSocket(this, socket, options)
            };
            Connection connection = CreateConnection(multiStreamSocket, options, server: null);
            await connection.ConnectAsync(authenticationOptions, cancel).ConfigureAwait(false);
            return connection;
        }

        protected internal virtual Connection CreateConnection(
            MultiStreamOverSingleStreamSocket socket,
            ConnectionOptions options,
            Server? server) =>
            new TcpConnection(this, socket, options, server);

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

        internal virtual SingleStreamSocket CreateSocket(EndPoint addr, TcpOptions options, ILogger logger)
        {
            // We still specify the address family for the socket if an address is set to ensure an IPv4 socket is
            // created if the address is an IPv4 address.
            Socket socket = HasDnsHost ?
                new Socket(SocketType.Stream, ProtocolType.Tcp) :
                new Socket(Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !options.IsIPv6Only;
                }

                if (options.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    socket.Bind(localEndPoint);
                }

                SetBufferSize(socket, options.ReceiveBufferSize, options.SendBufferSize, logger);
                socket.NoDelay = true;
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex, RetryPolicy.OtherReplica);
            }

            return new TcpSocket(socket, logger, addr);
        }

        internal virtual SingleStreamSocket CreateSocket(Socket socket, ILogger logger) =>
            new TcpSocket(socket, logger);
    }
}
