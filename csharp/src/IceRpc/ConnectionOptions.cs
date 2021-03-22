// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Security;

namespace IceRpc
{
    public sealed class SlicOptions
    {
        private int _packetMaxSize = 32 * 1024;
        private int? _streamBufferMaxSize;

        public int PacketMaxSize
        {
            get => _packetMaxSize;
            set => _packetMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(PacketMaxSize)} cannot be less than 1KB", nameof(value));
        }

        public int StreamBufferMaxSize
        {
             get => _streamBufferMaxSize ?? 2 * PacketMaxSize;
             set => _streamBufferMaxSize = value >= 1024 ? value :
                throw new ArgumentException($"{nameof(StreamBufferMaxSize)} cannot be less than 1KB", nameof(value));
        }

        public SlicOptions Clone() => (SlicOptions)MemberwiseClone();
    }

    public sealed class SocketOptions
    {
        private int _tcpBackLog = 511;
        private int? _tcpReceiveBufferSize;
        private int? _tcpSendBufferSize;
        private int? _udpReceiveBufferSize;
        private int? _udpSendBufferSize;

        public IPAddress? SourceAddress { get; set; }

        public int TcpBackLog
        {
            get => _tcpBackLog;
            set => _tcpBackLog = value > 0 ? value :
                throw new ArgumentException($"{nameof(TcpBackLog)} can't be less than 1", nameof(value));
        }

        public int? TcpReceiveBufferSize
        {
            get => _tcpReceiveBufferSize;
            set => _tcpReceiveBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(TcpReceiveBufferSize)} can't be less than 1KB", nameof(value));
        }

        public int? TcpSendBufferSize
        {
            get => _tcpSendBufferSize;
            set => _tcpSendBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(TcpSendBufferSize)} can't be less than 1KB", nameof(value));
        }

        public int? UdpReceiveBufferSize
        {
            get => _udpReceiveBufferSize;
            set => _udpReceiveBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(UdpReceiveBufferSize)} can't be less than 1KB", nameof(value));
        }

        public int? UdpSendBufferSize
        {
            get => _udpSendBufferSize;
            set => _udpSendBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(UdpSendBufferSize)} can't be less than 1KB", nameof(value));
        }

        public SocketOptions Clone() => (SocketOptions)MemberwiseClone();
    }

    public abstract class ConnectionOptions
    {
        private int _bidirectionalStreamMaxCount = 100;
        private TimeSpan _closeTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
        private int _incomingFrameMaxSize = 1024 * 1024;
        private int _unidirectionalStreamMaxCount = 100;

        public int BidirectionalStreamMaxCount
        {
            get => _bidirectionalStreamMaxCount;
            set => _bidirectionalStreamMaxCount = value > 0 ? value :
                throw new InvalidArgumentException(
                    $"{nameof(BidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        public SlicOptions SlicOptions { get; set; } = new SlicOptions();

        public SocketOptions SocketOptions { get; set; } = new SocketOptions();

        public TimeSpan CloseTimeout
        {
            get => _closeTimeout;
            set => _closeTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(CloseTimeout)}", nameof(value));
        }

        public TimeSpan IdleTimeout
        {
            get => _idleTimeout;
            set => _idleTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(IdleTimeout)}", nameof(value));
        }

        public int IncomingFrameMaxSize
        {
            get => _incomingFrameMaxSize;
            set => _incomingFrameMaxSize = value >= 1024 ? value :
                value == 0 ? int.MaxValue : // TODO: remove this?
                throw new ArgumentException($"{nameof(IncomingFrameMaxSize)} cannot be less than 1KB ", nameof(value));
        }

        public bool KeepAlive { get; set; }

        public int UnidirectionalStreamMaxCount
        {
            get => _unidirectionalStreamMaxCount;
            set => _unidirectionalStreamMaxCount = value > 0 ? value :
                throw new InvalidArgumentException(
                    $"{nameof(UnidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        // The logger factory is internal for now. It's set either with the ServerOptions or Communicator logger
        // factory by the Server/Communicator classes.
        internal ILoggerFactory? LoggerFactory { get; set; }

        internal ILogger ProtocolLogger => LoggerFactory!.CreateLogger("IceRpc.Protocol");

        internal ILogger TransportLogger => LoggerFactory!.CreateLogger("IceRpc.Protocol");

        protected ConnectionOptions Clone()
        {
            var options = (ConnectionOptions)MemberwiseClone();
            options.SlicOptions = SlicOptions.Clone();
            options.SocketOptions = SocketOptions.Clone();
            return options;
        }
    }

    public sealed class OutgoingConnectionOptions : ConnectionOptions
    {
        private SslClientAuthenticationOptions? _authenticationOptions;
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

        public SslClientAuthenticationOptions? AuthenticationOptions
        {
            get => _authenticationOptions;
            set => _authenticationOptions = value?.Clone();
        }

        public TimeSpan ConnectTimeout
        {
            get => _connectTimeout;
            set => _connectTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(ConnectTimeout)}", nameof(value));
        }

        public object? Label { get; set; }

        /// <summary>Indicates under what conditions this server accepts non-secure connections.</summary>
        // TODO: switch to NonSecure.Never default
        public NonSecure PreferNonSecure { get; set; } = NonSecure.Always;

        public new OutgoingConnectionOptions Clone()
        {
            OutgoingConnectionOptions options = (OutgoingConnectionOptions)base.Clone();
            options.AuthenticationOptions = AuthenticationOptions;
            return options;
        }
    }

    public sealed class IncomingConnectionOptions : ConnectionOptions
    {
        private SslServerAuthenticationOptions? _authenticationOptions;
        private TimeSpan _acceptTimeout = TimeSpan.FromSeconds(10);

        public SslServerAuthenticationOptions? AuthenticationOptions
        {
            get => _authenticationOptions;
            set => _authenticationOptions = value?.Clone();
        }

        /// <summary>Indicates under what circumstances this server accepts non-secure incoming connections.
        /// </summary>
        // TODO: fix default
        public NonSecure AcceptNonSecure { get; set; } = NonSecure.Always;

        public TimeSpan AcceptTimeout
        {
            get => _acceptTimeout;
            set => _acceptTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(AcceptTimeout)}", nameof(value));
        }

        public new IncomingConnectionOptions Clone()
        {
            IncomingConnectionOptions options = (IncomingConnectionOptions)base.Clone();
            options.AuthenticationOptions = AuthenticationOptions;
            return options;
        }
    }
}
