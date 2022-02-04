// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.Net;
using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>The base options class for TCP transports.</summary>
    public class TcpOptions
    {
        /// <summary>The idle timeout. This timeout is used to monitor the network connection. If the connection
        /// is idle within this timeout period, the connection is gracefully closed. It can't be 0 and the default
        /// value is 60s.</summary>
        /// <value>The network connection idle timeout value.</value>
        public TimeSpan IdleTimeout
        {
            get => _idleTimeout;
            init => _idleTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(IdleTimeout)}", nameof(value));
        }

        /// <summary>Configures an IPv6 socket to only support IPv6. The socket won't support IPv4 mapped addresses
        /// when this property is set to true. The default value is false.</summary>
        /// <value>The boolean value to enable or disable IPv6-only support.</value>
        public bool IsIPv6Only { get; init; }

        /// <summary>Gets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
        /// <value>A pool of memory blocks used for buffer management.</value>
        public MemoryPool<byte> Pool { get; init; } = MemoryPool<byte>.Shared;

        /// <summary>Gets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
        /// <value>The minimum size of the segment requested from the <see cref="Pool" />.</value>
        public int MinimumSegmentSize
        {
            get => _minimumSegmentSize;
            init => _minimumSegmentSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(MinimumSegmentSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>The socket receive buffer size in bytes. It can't be less than 1KB. If not set, the OS default
        /// receive buffer size is used.</summary>
        /// <value>The receive buffer size in bytes.</value>
        public int? ReceiveBufferSize
        {
            get => _receiveBufferSize;
            init => _receiveBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(ReceiveBufferSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>The socket send buffer size in bytes. It can't be less than 1KB. If not set, the OS default send
        /// buffer size is used.</summary>
        /// <value>The send buffer size in bytes.</value>
        public int? SendBufferSize
        {
            get => _sendBufferSize;
            init => _sendBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(SendBufferSize)} can't be less than 1KB", nameof(value));
        }

        private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
        private int _minimumSegmentSize = 4096;
        private int? _receiveBufferSize;
        private int? _sendBufferSize;
    }

    /// <summary>The options class for configuring <see cref="TcpClientTransport"/>.</summary>
    public sealed class TcpClientOptions : TcpOptions
    {
        /// <summary>The SSL authentication options. If null, ssl/tls is disabled.</summary>
        public SslClientAuthenticationOptions? AuthenticationOptions { get; init; }

        /// <summary>The address and port represented by a .NET IPEndPoint to use for a client socket. If
        /// specified the client socket will bind to this address and port before connection
        /// establishment.</summary>
        /// <value>The address and port to bind the socket to.</value>
        public IPEndPoint? LocalEndPoint { get; init; }
    }

    /// <summary>The options class for configuring <see cref="TcpServerTransport"/>.</summary>
    public sealed class TcpServerOptions : TcpOptions
    {
        /// <summary>The SSL authentication options. If null, ssl/tls is disabled.</summary>
        public SslServerAuthenticationOptions? AuthenticationOptions { get; init; }

        /// <summary>Configures the length of a server socket queue for accepting new connections. If a new connection
        /// request arrives and the queue is full, the client connection establishment will fail with a
        /// <see cref="ConnectionRefusedException"/> exception. The default value is 511.</summary>
        /// <value>The server socket backlog size.</value>
        public int ListenerBackLog
        {
            get => _listenerBackLog;
            init => _listenerBackLog = value > 0 ? value :
                throw new ArgumentException($"{nameof(ListenerBackLog)} can't be less than 1", nameof(value));
        }

        private int _listenerBackLog = 511;
    }
}
