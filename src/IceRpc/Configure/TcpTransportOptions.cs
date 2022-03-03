// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net;

namespace IceRpc.Configure
{
    /// <summary>The base options class for TCP transports.</summary>
    public record class TcpTransportOptions
    {
        /// <summary>Gets or initializes the idle timeout. This timeout is used to monitor the network connection. If
        /// the connection is idle within this timeout period, the connection is gracefully closed.</summary>
        /// <value>The network connection idle timeout value. The default is 60s.</value>
        public TimeSpan IdleTimeout
        {
            get => _idleTimeout;
            init => _idleTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(IdleTimeout)}", nameof(value));
        }

        /// <summary>Configures an IPv6 socket to only support IPv6. The socket won't support IPv4 mapped addresses
        /// when this property is set to <c>true</c>.</summary>
        /// <value><c>true</c> to enable IPv6-only support, <c>false</c> to disable it. The default is <c>false</c>.
        /// </value>
        public bool IsIPv6Only { get; init; }

        /// <summary>Gets or initializes the socket receive buffer size in bytes.</summary>
        /// <value>The receive buffer size in bytes. It can't be less than 1KB. <c>null</c> means use the operating
        /// system default.</value>
        public int? ReceiveBufferSize
        {
            get => _receiveBufferSize;
            init => _receiveBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(ReceiveBufferSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>Gets or initializes the socket send buffer size in bytes.</summary>
        /// <value>The send buffer size in bytes. It can't be less than 1KB. <c>null</c> means use the OS default.
        /// </value>
        public int? SendBufferSize
        {
            get => _sendBufferSize;
            init => _sendBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(SendBufferSize)} can't be less than 1KB", nameof(value));
        }

        private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
        private int? _receiveBufferSize;
        private int? _sendBufferSize;
    }

    /// <summary>The options class for configuring <see cref="TcpClientTransport"/>.</summary>
    public sealed record class TcpClientTransportOptions : TcpTransportOptions
    {
        /// <summary>Gets or initializes the address and port represented by a .NET IPEndPoint to use for a client
        /// socket. If specified the client socket will bind to this address and port before connection establishment.
        /// </summary>
        /// <value>The address and port to bind the socket to.</value>
        public IPEndPoint? LocalEndPoint { get; init; }
    }

    /// <summary>The options class for configuring <see cref="TcpServerTransport"/>.</summary>
    public sealed record class TcpServerTransportOptions : TcpTransportOptions
    {
        /// <summary>Gets or initializes the length of the server socket queue for accepting new connections. If a new
        /// connection request arrives and the queue is full, the client connection establishment will fail with a
        /// <see cref="ConnectionRefusedException"/> exception.</summary>
        /// <value>The server socket backlog size. The default is 511</value>
        public int ListenerBackLog
        {
            get => _listenerBackLog;
            init => _listenerBackLog = value > 0 ? value :
                throw new ArgumentException($"{nameof(ListenerBackLog)} can't be less than 1", nameof(value));
        }

        private int _listenerBackLog = 511;
    }
}
