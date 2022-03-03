// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net;

namespace IceRpc.Configure
{
    /// <summary>The options class for configuring <see cref="UdpClientTransport"/>.</summary>
    public sealed record class UdpClientTransportOptions
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

        /// <summary>The address and port represented by a .NET IPEndPoint to use for a client socket. If specified the
        /// client socket will bind to this address and port before connection establishment.</summary>
        /// <value>The address and port to bind the socket to.</value>
        public IPEndPoint? LocalEndPoint { get; init; }

        /// <summary>The socket send buffer size in bytes. It can't be less than 1KB. If not set, the OS default
        /// send buffer size is used.</summary>
        /// <value>The send buffer size in bytes.</value>
        public int? SendBufferSize
        {
            get => _sendBufferSize;
            init => _sendBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(SendBufferSize)} can't be less than 1KB", nameof(value));
        }

        private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);

        private int? _sendBufferSize;
    }
}
