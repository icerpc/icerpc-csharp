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
                _ => base[option],
            };

        private protected bool HasCompressionFlag { get; }
        private protected TimeSpan Timeout { get; } = DefaultTimeout;

        /// <summary>The default timeout for ice1 endpoints.</summary>
        protected static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        private int _equivalentHashCode;
        private int _hashCode;

        // TODO: should not be public
        public override IAcceptor Acceptor(Server server)
        {
            Debug.Assert(Address != IPAddress.None); // i.e. not a DNS name

            var address = new IPEndPoint(Address, Port);
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                var socketOptions = server.ConnectionOptions.SocketOptions!;
                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, socketOptions.IsIPv6Only);
                }
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);

                socket.Bind(address);
                address = (IPEndPoint)socket.LocalEndPoint!;
                socket.Listen(socketOptions.ListenerBackLog);
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
                return base.Equals(other);
            }
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
                int hashCode;
                if (Protocol == Protocol.Ice1)
                {
                    hashCode = HashCode.Combine(base.GetHashCode(), HasCompressionFlag, Timeout);
                }
                else
                {
                    hashCode = base.GetHashCode();
                }

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
        }

        protected internal override int GetEquivalentHashCode()
        {
            // This code is thread safe because reading/writing _hashCode (an int) is atomic.
            if (_equivalentHashCode != 0)
            {
                // Return cached value
                return _equivalentHashCode;
            }
            else
            {
                int hashCode = base.GetHashCode();
                if (hashCode == 0) // 0 is not a valid value as it means "not initialized".
                {
                    hashCode = 1;
                }
                _equivalentHashCode = hashCode;
                return _equivalentHashCode;
            }
        }

        // We ignore the Timeout and HasCompressionFlag properties when checking if two TCP endpoints are equivalent.
        protected internal override bool IsEquivalent(Endpoint? other) =>
            ReferenceEquals(this, other) || base.Equals(other);

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
            bool serverEndpoint,
            string endpointString)
        {
            Debug.Assert(transport == Transport.TCP || transport == Transport.SSL);
            (string host, ushort port) = ParseHostAndPort(options, serverEndpoint, endpointString);
            return new TcpEndpoint(new EndpointData(transport, host, port, Array.Empty<string>()),
                                   ParseTimeout(options, endpointString),
                                   ParseCompress(options, endpointString),
                                   serverEndpoint);
        }

        internal static TcpEndpoint ParseIce2Endpoint(
            Transport transport,
            string host,
            ushort port,
            Dictionary<string, string> _,
            bool serverEndpoint)
        {
            Debug.Assert(transport == Transport.TCP || transport == Transport.SSL);
            return new TcpEndpoint(new EndpointData(transport, host, port, Array.Empty<string>()), serverEndpoint);
        }

        protected internal override async Task<Connection> ConnectAsync(
            OutgoingConnectionOptions options,
            ILogger protocolLogger,
            ILogger transportLogger,
            CancellationToken cancel)
        {
            // If the endpoint is always secure or a secure connection is required, connect with the SSL client
            // authentication options.
            SslClientAuthenticationOptions? authenticationOptions = null;
            if (IsAlwaysSecure || options.NonSecure switch
            {
                NonSecure.SameHost => true,    // TODO check if Host is the same host
                NonSecure.TrustedHost => true, // TODO check if Host is a trusted host
                NonSecure.Always => false,
                _ => true
            })
            {
                authenticationOptions = options.AuthenticationOptions ?? new SslClientAuthenticationOptions()
                {
                    TargetHost = Host
                };
            }

            EndPoint endpoint = HasDnsHost ? new DnsEndPoint(Host, Port) : new IPEndPoint(Address, Port);
            SingleStreamSocket socket = CreateSocket(endpoint, options.SocketOptions!, transportLogger);
            MultiStreamOverSingleStreamSocket multiStreamSocket = Protocol switch
            {
                Protocol.Ice1 => new Ice1NetworkSocket(this, socket, options, protocolLogger),
                _ => new SlicSocket(this, socket, options, protocolLogger)
            };
            Connection connection = CreateConnection(multiStreamSocket, options, server: null);
            await connection.Socket.ConnectAsync(authenticationOptions, cancel).ConfigureAwait(false);
            Debug.Assert(connection.CanTrust(options.NonSecure));
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
                    throw new FormatException($"no argument provided for -t option in endpoint `{endpointString}'");
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
                            $"invalid timeout value `{argument}' in endpoint `{endpointString}'",
                            ex);
                    }
                    if (timeout <= TimeSpan.Zero)
                    {
                        throw new FormatException($"invalid timeout value `{argument}' in endpoint `{endpointString}'");
                    }
                }
                options.Remove("-t");
            }
            return timeout;
        }

        // Constructor for ice1 unmarshaling.
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

        // Constructor for ice1 parsing.
        private protected TcpEndpoint(EndpointData data, TimeSpan timeout, bool compress, bool serverEndpoint)
            : base(data, serverEndpoint, Protocol.Ice1)
        {
            Timeout = timeout;
            HasCompressionFlag = compress;
        }

        // Constructor for ice2 parsing.
        private protected TcpEndpoint(EndpointData data, bool serverEndpoint)
            : base(data, serverEndpoint, Protocol.Ice2)
        {
        }

        // Clone constructor
        private protected TcpEndpoint(TcpEndpoint endpoint, string host, ushort port)
            : base(endpoint, host, port)
        {
            HasCompressionFlag = endpoint.HasCompressionFlag;
            Timeout = endpoint.Timeout;
        }

        private protected override IPEndpoint Clone(string host, ushort port) =>
            new TcpEndpoint(this, host, port);

        internal virtual SingleStreamSocket CreateSocket(EndPoint addr, SocketOptions options, ILogger logger)
        {
            // We still specify the address family for the socket if an address is set to ensure an IPv4 socket is
            // created if the address is an IPv4 address.
            var socket = HasDnsHost ?
                new Socket(SocketType.Stream, ProtocolType.Tcp) :
                new Socket(Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                if (Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, options.IsIPv6Only);
                }

                if (options.SourceAddress is IPAddress sourceAddress)
                {
                    socket.Bind(new IPEndPoint(sourceAddress, 0));
                }
                SetBufferSize(socket, options.ReceiveBufferSize, options.SendBufferSize, logger);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, 1);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex, RetryPolicy.OtherReplica);
            }

            return new TcpSocket(socket, logger, addr);
        }

        internal virtual SingleStreamSocket CreateSocket(Socket socket, SocketOptions options, ILogger logger)
        {
            try
            {
                SetBufferSize(socket, options.ReceiveBufferSize, options.SendBufferSize, logger);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex, RetryPolicy.OtherReplica);
            }
            return new TcpSocket(socket, logger);
        }
    }
}
