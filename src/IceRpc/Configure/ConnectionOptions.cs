// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Configure
{
    /// <summary>A property bag used to configure a client <see cref="Connection"/>.</summary>
    public sealed record class ConnectionOptions
    {
        /// <summary>Returns the default value for <see cref="Dispatcher"/>.</summary>
        public static IDispatcher DefaultDispatcher { get; } = new InlineDispatcher(
            (request, cancel) => throw new DispatchException(
                DispatchErrorCode.ServiceNotFound,
                RetryPolicy.OtherReplica));

        /// <summary>Returns the default value for <see cref="MultiplexedClientTransport"/>.</summary>
        public static IClientTransport<IMultiplexedNetworkConnection> DefaultMultiplexedClientTransport { get; } =
            new CompositeMultiplexedClientTransport().UseSlicOverTcp();

        /// <summary>Returns the default value for <see cref="SimpleClientTransport"/>.</summary>
        public static IClientTransport<ISimpleNetworkConnection> DefaultSimpleClientTransport { get; } =
            new CompositeSimpleClientTransport().UseTcp().UseUdp();

        /// <summary>Gets or initializes the connection close timeout. This timeout is used when gracefully closing a
        /// connection to wait for the peer connection closure. If the peer doesn't close its side of the connection
        /// within the timeout timeframe, the connection is forcefully closed.</summary>
        /// <value>The close timeout value. The default is 10s.</value>
        public TimeSpan CloseTimeout
        {
            get => _closeTimeout;
            init => _closeTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(CloseTimeout)}", nameof(value));
        }

        /// <summary>Gets or initializes the connection establishment timeout.</summary>
        /// <value>The connection establishment timeout value. The default is 10s.</value>
        public TimeSpan ConnectTimeout
        {
            get => _connectTimeout;
            init => _connectTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(ConnectTimeout)}", nameof(value));
        }

        /// <summary>Gets or initializes the dispatcher that dispatches requests received by this connection.</summary>
        /// <value>The dispatcher that dispatches requests received by this connection.</value>
        public IDispatcher Dispatcher { get; init; } = DefaultDispatcher;

        /// <summary>Gets or initializes the maximum size in bytes of an incoming Ice or IceRpc protocol frame. It can't
        /// be less than 1KB and the default value is 1MB.</summary>
        /// <value>The maximum size of incoming frame in bytes.</value>
        public int IncomingFrameMaxSize
        {
            get => _incomingFrameMaxSize;
            init => _incomingFrameMaxSize = value >= 1024 ? value :
                value <= 0 ? int.MaxValue :
                throw new ArgumentException($"{nameof(IncomingFrameMaxSize)} cannot be less than 1KB ", nameof(value));
        }

        /// <summary>Specifies if the connection can be resumed after being closed.</summary>
        /// <value>When <c>true</c>, the connection will be re-established by the next call to
        /// <see cref="Connection.ConnectAsync"/> or the next invocation. The <see cref="Connection.State"/> is always
        /// switched back to <see cref="ConnectionState.NotConnected"/> after the connection closure. When <c>false</c>,
        /// the <see cref="Connection.State"/> is <see cref="ConnectionState.Closed"/> once the connection is closed and
        /// the connection won't be resumed. The default value is <c>false</c>.</value>
        public bool IsResumable { get; init; }

        /// <summary>Gets or initializes the connection's keep alive. If a connection is kept alive, the connection
        /// monitoring will send keep alive frames to ensure the peer doesn't close the connection in the period defined
        /// by its idle timeout. How often keep alive frames are sent depends on the peer's IdleTimeout configuration.
        /// </summary>
        /// <value><c>true</c>to enable connection keep alive. <c>false</c> to disable it. The default is <c>false</c>.
        /// </value>
        public bool KeepAlive { get; init; }

        /// <summary>Gets or initializes the logger factory used to create loggers to log connection-related activities.
        /// </summary>
        public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

        /// <summary>Gets or initializes the <see cref="IClientTransport{IMultiplexedNetworkConnection}"/> used by this
        /// connection to create multiplexed network connections.</summary>
        public IClientTransport<IMultiplexedNetworkConnection> MultiplexedClientTransport { get; init; } =
            DefaultMultiplexedClientTransport;

        /// <summary>Gets or initializes the connection's remote endpoint.</summary>
        public Endpoint? RemoteEndpoint
        {
            get => _remoteEndpoint;
            init
            {
                if (value is Endpoint remoteEndpoint)
                {
                    _remoteEndpoint = remoteEndpoint.Protocol.IsSupported ? remoteEndpoint :
                        throw new NotSupportedException(
                            $"cannot connect to endpoint with protocol '{remoteEndpoint.Protocol}'");
                }
                else
                {
                    _remoteEndpoint = null;
                }
            }
        }

        /// <summary>Gets or initializes the <see cref="IClientTransport{ISimpleNetworkConnection}"/> used by this
        /// connection to create simple network connections.</summary>
        public IClientTransport<ISimpleNetworkConnection> SimpleClientTransport { get; init; } =
            DefaultSimpleClientTransport;

        private TimeSpan _closeTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
        private int _incomingFrameMaxSize = 1024 * 1024;

        private Endpoint? _remoteEndpoint;
    }
}
