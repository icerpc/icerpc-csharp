// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc
{
    /// <summary>An options base class for configuring IceRPC connections.</summary>
    public abstract class ConnectionOptions
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

        /// <summary>Gets or sets the class factory used for instantiating classes decoded from requests or responses.
        /// </summary>
        public IClassFactory? ClassFactory { get; set; }

        /// <summary>Configures the maximum depth for a graph of Slice class instances to unmarshal. When the limit is reached,
        /// the IceRpc run time throws <see cref="InvalidDataException"/>.</summary>
        /// <value>The maximum depth for a graph of Slice class instances to unmarshal.</value>
        public int ClassGraphMaxDepth
        {
            get => _classGraphMaxDepth;
            set => _classGraphMaxDepth = value < 1 ? int.MaxValue : value;
        }

        /// <summary>The connection close timeout. This timeout is used when gracefully closing a connection to
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

        /// <summary>The features of the connection.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>The connection idle timeout. This timeout is used to monitor the connection. If the connection
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
        /// connection monitoring will send keep alive frames to ensure the peer doesn't close the connection
        /// in the period defined by its idle timeout. How often keep alive frames are sent depends on the
        /// peer's IdleTimeout configuration. The default value is false.</summary>
        /// <value>Enables connection keep alive.</value>
        public bool KeepAlive { get; set; }

        /// <summary>Gets or sets the class factory used for instantiating classes decoded from requests or responses.
        /// </summary>
        public IRemoteExceptionFactory? RemoteExceptionFactory { get; set; }

        /// <summary>Configures the unidirectional stream maximum count to limit the number of concurrent unidirectional
        /// streams opened on a connection. When this limit is reached, trying to open a new unidirectional stream
        /// will be delayed until an unidirectional stream is closed. Since an unidirectional stream is opened for
        /// each one-way proxy invocation, the sending of the one-way invocation will be delayed until another one-way
        /// invocation on the connection completes. It can't be less than 1 and the default value is 100.</summary>
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
        private int _classGraphMaxDepth = 100;
        private TimeSpan _closeTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
        private int _incomingFrameMaxSize = 1024 * 1024;
        private int _unidirectionalStreamMaxCount = 100;

        /// <inheritdoc/>
        protected internal ConnectionOptions Clone() => (ConnectionOptions)MemberwiseClone();
    }

    /// <summary>An options class for configuring outgoing IceRPC connections.</summary>
    public sealed class ClientConnectionOptions : ConnectionOptions
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

        internal static ClientConnectionOptions Default = new();

        private SslClientAuthenticationOptions? _authenticationOptions;
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);

        /// <inheritdoc/>
        public new ClientConnectionOptions Clone()
        {
            var options = (ClientConnectionOptions)base.Clone();
            options.AuthenticationOptions = AuthenticationOptions;
            return options;
        }
    }

    /// <summary>An options class for configuring incoming IceRPC connections.</summary>
    public sealed class ServerConnectionOptions : ConnectionOptions
    {
        /// <summary>The SSL authentication options to configure TLS server connections.</summary>
        /// <value>The SSL authentication options.</value>
        public SslServerAuthenticationOptions? AuthenticationOptions
        {
            get => _authenticationOptions;
            set => _authenticationOptions = value?.Clone();
        }

        /// <summary>The connection accept timeout. If a new server connection takes longer than the accept timeout to
        /// be initialized, the server will abandon and close the connection. It can't be 0 and the default value is
        /// 10s.</summary>
        /// <value>The connection accept timeout value.</value>
        public TimeSpan AcceptTimeout
        {
            get => _acceptTimeout;
            set => _acceptTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(AcceptTimeout)}", nameof(value));
        }

        internal static ServerConnectionOptions Default = new();

        private TimeSpan _acceptTimeout = TimeSpan.FromSeconds(10);
        private SslServerAuthenticationOptions? _authenticationOptions;

        /// <inheritdoc/>
        public new ServerConnectionOptions Clone()
        {
            var options = (ServerConnectionOptions)base.Clone();
            options.AuthenticationOptions = AuthenticationOptions;
            return options;
        }
    }
}
