// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;

namespace IceRpc.Transports
{
    /// <summary>An options class for configuring TCP based transports.</summary>
    public sealed class TcpOptions : SlicOptions
    {
        /// <summary>Configures the bidirectional stream maximum count to limit the number of concurrent bidirectional
        /// streams opened on a connection. When this limit is reached, trying to open a new bidirectional stream
        /// will be delayed until an bidirectional stream is closed. Since an bidirectional stream is opened for
        /// each two-way proxy invocation, the sending of the two-way invocation will be delayed until another two-way
        /// invocation on the connection completes. It can't be less than 1 and the default value is 100.</summary>
        /// <value>The bidirectional stream maximum count.</value>
        public int BidirectionalStreamMaxCount
        {
            get => _bidirectionalStreamMaxCount;
            set => _bidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(BidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

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

        private int _listenerBackLog = 511;
        private int? _receiveBufferSize;
        private int? _sendBufferSize;
        private int _unidirectionalStreamMaxCount = 100;
    }
}
