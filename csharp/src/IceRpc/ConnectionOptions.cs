// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Net;
using System.Net.Security;

namespace IceRpc
{
    public sealed class SlicOptions
    {
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

        private int _packetMaxSize = 32 * 1024;
        private int? _streamBufferMaxSize;

        public SlicOptions Clone() => (SlicOptions)MemberwiseClone();
    }

    public sealed class SocketOptions
    {
        public IPAddress? SourceAddress { get; set; }

        public int TcpBackLog
        {
            get => _tcpBackLog;
            set => _tcpBackLog = value > 0 ? value :
                throw new ArgumentException($"{nameof(TcpBackLog)} can't be less than 1", nameof(value));
        }

        public int? ReceiveBufferSize
        {
            get => _receiveBufferSize;
            set => _receiveBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(ReceiveBufferSize)} can't be less than 1KB", nameof(value));
        }

        public int? SendBufferSize
        {
            get => _sendBufferSize;
            set => _sendBufferSize = value == null || value >= 1024 ? value :
                throw new ArgumentException($"{nameof(SendBufferSize)} can't be less than 1KB", nameof(value));
        }

        private int _tcpBackLog = 511;
        private int? _receiveBufferSize;
        private int? _sendBufferSize;

        public SocketOptions Clone() => (SocketOptions)MemberwiseClone();
    }

    public abstract class ConnectionOptions
    {
        public int BidirectionalStreamMaxCount
        {
            get => _bidirectionalStreamMaxCount;
            set => _bidirectionalStreamMaxCount = value > 0 ? value :
                throw new InvalidArgumentException(
                    $"{nameof(BidirectionalStreamMaxCount)} can't be less than 1",
                    nameof(value));
        }

        public SlicOptions? SlicOptions { get; set; }

        public SocketOptions? SocketOptions { get; set; }

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
                value <= 0 ? int.MaxValue :
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

    public sealed class OutgoingConnectionOptions : ConnectionOptions
    {
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

        // TODO: switch to NonSecure.Never default
        public NonSecure PreferNonSecure { get; set; } = NonSecure.Always;

        private SslClientAuthenticationOptions? _authenticationOptions;
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

        public new OutgoingConnectionOptions Clone()
        {
            OutgoingConnectionOptions options = (OutgoingConnectionOptions)base.Clone();
            options.AuthenticationOptions = AuthenticationOptions;
            return options;
        }
    }

    public sealed class IncomingConnectionOptions : ConnectionOptions
    {
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
