// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Net;
using System.Net.Security;

namespace IceRpc
{
    /// <summary>An options class for configuring the Slic transport.</summary>
    public sealed class SlicOptions
    {
        /// <summary>The Slic packet maximum size in bytes. It can't be less than 1KB and the default value
        /// is 32KB.</summary>
        /// <value>The packet maximum size in bytes.</value>
        public int PacketMaxSize
        {
            get => _packetMaxSize;
            set => _packetMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PacketMaxSize)} cannot be less than 1KB", nameof(value));
        }

        /// <summary>The Slic stream buffer maximum size in bytes. The stream buffer is used when streaming data with
        /// a stream Slice parameter. It can't be less than 1KB and the default value is twice the Slic packet maximum
        /// size.</summary>
        /// <value>The stream buffer maximum size in bytes.</value>
        public int StreamBufferMaxSize
        {
             get => _streamBufferMaxSize ?? 2 * PacketMaxSize;
             set => _streamBufferMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(StreamBufferMaxSize)} cannot be less than 1KB", nameof(value));
        }

        private int _packetMaxSize = 32 * 1024;
        private int? _streamBufferMaxSize;

        public SlicOptions Clone() => (SlicOptions)MemberwiseClone();
    }

    /// <summary>An options class for configuring Berkeley socket based transports (TCP and UDP based
    /// transports).</summary>
    public sealed class SocketOptions
    {
        /// <summary>Configures an IPv6 socket to only support IPv6. The socket won't support IPv4 mapped addresses
        /// when this property is set to true. The default value is false.</summary>
        /// <value>The boolean value to enable or disable IPv6-only support.</value>
        public bool IsIPv6Only { get; set; }

        /// <summary>The source address to use for a client socket. If specified, the client socket will bind to
        /// this address before connection establishment.</summary>
        /// <value>The IP address to bind the socket to.</value>
        public IPAddress? SourceAddress { get; set; }

        /// <summary>Configures the length of a server socket queue for accepting new connections. If a new
        /// connection request arrives and the queue is full, the client connection establishment will fail
        /// with a <see cref="ConnectionRefusedException"/> exception. The default value is 511.</summary>
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

        /// <summary>The socket send buffer size in bytes. It can't be less than 1KB. If not set, the OS default
        /// send buffer size is used.</summary>
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

        public SocketOptions Clone() => (SocketOptions)MemberwiseClone();
    }

    /// <summary>An options base class for configuring IceRPC connections.</summary>
    public abstract class ConnectionOptions
    {
        /// <summary>Configures the bidirectional stream maximum count to limit the number of concurrent streams
        /// opened on a connection. When this limit is reached, trying to open a new stream will be delayed until
        /// a stream is closed. Since a bidirectional stream is opened for each two-way proxy invocation, the
        /// sending of the invocation will be delayed until another two-way request on the connection completes.
        /// It can't be less than 1 and the default value is 100.</summary>
        /// <value>The bidirectional stream maximum count.</value>
        public int BidirectionalStreamMaxCount
        {
            get => _bidirectionalStreamMaxCount;
            set => _bidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(BidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        /// <summary>The options for Slic based transports (TCP based transports).</summary>
        /// <value>The Slic options.</value>
        public SlicOptions? SlicOptions { get; set; }

        /// <summary>The options for Berkeley socket based transports (TCP and UDP based transports).</summary>
        /// <value>The socket options.</value>
        public SocketOptions? SocketOptions { get; set; }

        /// <summary>The connection close timeout. This timeout if used when gracefully closing a connection to
        /// wait for the peer connection closure. If the peer doesn't close its side of the connection within the
        /// timeout timeframe, the connection is forcefully closed. It can't be 0 and the default value is 10s.
        /// </summary>
        /// <value>The close timeout value.</value>
        public TimeSpan CloseTimeout
        {
            get => _closeTimeout;
            set => _closeTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(CloseTimeout)}", nameof(value));
        }

        /// <summary>The connection idle timeout. This timeout if used for monitor the connection. If the connection
        /// is idle within this timeout period, the connection is gracefully closed. It can't be 0 and the default
        /// value is 60s.</summary>
        /// <value>The connection idle timeout value.</value>
        public TimeSpan IdleTimeout
        {
            get => _idleTimeout;
            set => _idleTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(IdleTimeout)}", nameof(value));
        }

        /// <summary>The maximum size in bytes of an incoming Ice1 or Ice2 protocol frame. It's important to specify
        /// a reasonable value for this size since it limits the size of the buffer allocated by IceRPC to receive
        /// a request or response. It can't be less than 1KB and the default value is 1MB.</summary>
        /// <value>The maximum size of incoming frame in bytes.</value>
        public int IncomingFrameMaxSize
        {
            get => _incomingFrameMaxSize;
            set => _incomingFrameMaxSize = value >= 1024 ? value :
                value <= 0 ? int.MaxValue :
                throw new ArgumentException($"{nameof(IncomingFrameMaxSize)} cannot be less than 1KB ", nameof(value));
        }

        /// <summary>Configures whether or not connections are kept alive. If a connection is kept alive, the
        /// connection monitoring won't close the connection after the idle timeout period. The default value
        /// is false.</summary>
        /// <value>Enables connection keep alive.</value>
        public bool KeepAlive { get; set; }

        /// <summary>Configures the unidirectional stream maximum count to limit the number of concurrent streams
        /// opened on a connection. When this limit is reached, trying to open a new stream will be delayed until
        /// a stream is closed. Since a unidirectional stream is opened for each one-way proxy invocation, the
        /// sending of the invocation will be delayed until another one-way request on the connection completes.
        /// It can't be less than 1 and the default value is 100.</summary>
        /// <value>The unidirectional stream maximum count.</value>
        public int UnidirectionalStreamMaxCount
        {
            get => _unidirectionalStreamMaxCount;
            set => _unidirectionalStreamMaxCount = value > 0 ? value :
                throw new ArgumentException(
                    $"{nameof(UnidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        private int _bidirectionalStreamMaxCount = 100;
        private TimeSpan _closeTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
        private int _incomingFrameMaxSize = 1024 * 1024;
        private int _unidirectionalStreamMaxCount = 100;

        protected ConnectionOptions Clone()
        {
            var options = (ConnectionOptions)MemberwiseClone();
            options.SlicOptions = SlicOptions?.Clone();
            options.SocketOptions = SocketOptions?.Clone();
            return options;
        }
    }

    /// <summary>An options class for configuring client-side IceRPC connections.</summary>
    public sealed class OutgoingConnectionOptions : ConnectionOptions
    {
        /// <summary>The SSL authentication options to configure TLS client connections.</summary>
        /// <value>The SSL authentication options.</value>
        public SslClientAuthenticationOptions? AuthenticationOptions
        {
            get => _authenticationOptions;
            set => _authenticationOptions = value?.Clone();
        }

        /// <summary>The connection establishment timeout. It can't be 0 and the default value is 10s.</summary>
        /// <value>The connection establishment timeout value.</value>
        public TimeSpan ConnectTimeout
        {
            get => _connectTimeout;
            set => _connectTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(ConnectTimeout)}", nameof(value));
        }

        /// <summary>The NonSecure policy for establishing non-secure connections. The default value is
        /// <see cref="NonSecure.Always"/>.</summary>
        /// <value>The <see cref="NonSecure"/> enumeration value.</value>
        // TODO: switch to NonSecure.Never default
        public NonSecure NonSecure { get; set; } = NonSecure.Always;

        // TODO: Remove?
        internal object? Label { get; set; }

        private SslClientAuthenticationOptions? _authenticationOptions;
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

        public new OutgoingConnectionOptions Clone()
        {
            OutgoingConnectionOptions options = (OutgoingConnectionOptions)base.Clone();
            options.AuthenticationOptions = AuthenticationOptions;
            return options;
        }
    }

    /// <summary>An options class for configuring server-side IceRPC connections.</summary>
    public sealed class IncomingConnectionOptions : ConnectionOptions
    {
        /// <summary>The SSL authentication options to configure TLS server connections.</summary>
        /// <value>The SSL authentication options.</value>
        public SslServerAuthenticationOptions? AuthenticationOptions
        {
            get => _authenticationOptions;
            set => _authenticationOptions = value?.Clone();
        }

        /// <summary>The NonSecure policy for accepting non-secure connections. This indicates under what
        /// circumstances this server accepts non-secure incoming connections. The default value is
        /// <see cref="NonSecure.Always"/>.</summary>
        /// <value>The <see cref="NonSecure"/> enumeration value.</value>
        // TODO: fix default to NonSecure.Never
        public NonSecure AcceptNonSecure { get; set; } = NonSecure.Always;

        /// <summary>The connection accept timeout. If a new incoming connection takes longer than the accept timeout
        /// to be initialized, the server will abandon and close the connection. It can't be 0 and the default value
        /// is 10s.</summary>
        /// <value>The connection accept timeout value.</value>
        public TimeSpan AcceptTimeout
        {
            get => _acceptTimeout;
            set => _acceptTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(AcceptTimeout)}", nameof(value));
        }

        private TimeSpan _acceptTimeout = TimeSpan.FromSeconds(10);
        private SslServerAuthenticationOptions? _authenticationOptions;

        public new IncomingConnectionOptions Clone()
        {
            IncomingConnectionOptions options = (IncomingConnectionOptions)base.Clone();
            options.AuthenticationOptions = AuthenticationOptions;
            return options;
        }
    }
}
