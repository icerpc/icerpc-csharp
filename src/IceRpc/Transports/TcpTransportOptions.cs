// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Net;

namespace IceRpc.Transports
{
    /// <summary>An options class for configuring TCP based transports.</summary>
    public sealed class TcpTransportOptions
    {
        /// <summary>Configures an IPv6 socket to only support IPv6. The socket won't support IPv4 mapped addresses
        /// when this property is set to true. The default value is false.</summary>
        /// <value>The boolean value to enable or disable IPv6-only support.</value>
        public bool IsIPv6Only { get; set; }

        /// <summary>The address and port represented by a .NET IPEndPoint to use for a client socket. If specified the
        /// client socket will bind to this address and port before connection establishment.</summary>
        /// <value>The address and port to bind the socket to.</value>
        public IPEndPoint? LocalEndPoint { get; set; }

        /// <summary>Configures the length of a server socket queue for accepting new connections. If a new connection
        /// request arrives and the queue is full, the client connection establishment will fail with a
        /// <see cref="ConnectionRefusedException"/> exception. The default value is 511.</summary>
        /// <value>The server socket backlog size.</value>
        public int ListenerBackLog
        {
            get => _listenerBackLog;
            set => _listenerBackLog = value > 0 ? value :
                throw new ArgumentException($"{nameof(ListenerBackLog)} can't be less than 1", nameof(value));
        }

        /// <summary>The socket receive buffer size in bytes. It can't be less than 1KB. If not set, the OS default
        /// receive buffer size is used.</summary>
        /// <value>The receive buffer size in bytes.</value>
        public int? ReceiveBufferSize
        {
            get => _receiveBufferSize;
            set => _receiveBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(ReceiveBufferSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>The socket send buffer size in bytes. It can't be less than 1KB. If not set, the OS default send
        /// buffer size is used.</summary>
        /// <value>The send buffer size in bytes.</value>
        public int? SendBufferSize
        {
            get => _sendBufferSize;
            set => _sendBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(SendBufferSize)} can't be less than 1KB", nameof(value));
        }

        /// <summary>The Slic packet maximum size in bytes. It can't be less than 1KB and the default value is 32KB.
        /// Slic is only used for the Ice2 protocol, this setting is ignored when using the Ice1 protocol.</summary>
        /// <value>The Slic packet maximum size in bytes.</value>
        public int SlicPacketMaxSize
        {
            get => _slicPacketMaxSize;
            set => _slicPacketMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(SlicPacketMaxSize)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>The Slic stream buffer maximum size in bytes. The stream buffer is used when streaming data with
        /// a stream Slice parameter. It can't be less than 1KB and the default value is twice the Slic packet maximum
        /// size. Slic is only used for the Ice2 protocol, this setting is ignored when using the Ice1 protocol.
        /// </summary>
        /// <value>The Slic stream buffer maximum size in bytes.</value>
        public int SlicStreamBufferMaxSize
        {
            get => _slicStreamBufferMaxSize ?? 2 * SlicPacketMaxSize;
            set => _slicStreamBufferMaxSize = value >= 1024 ? value :
               throw new ArgumentException($"{nameof(SlicStreamBufferMaxSize)} cannot be less than 1KB", nameof(value));
        }

        internal static TcpTransportOptions Default = new();

        private int _listenerBackLog = 511;
        private int? _receiveBufferSize;
        private int? _sendBufferSize;
        private int _slicPacketMaxSize = 32 * 1024;
        private int? _slicStreamBufferMaxSize;
    }
}
