// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>A property bag used to configure a client <see cref="Connection"/>.</summary>
    public sealed record class ConnectionOptions
    {
        /// <summary>Returns the default value for <see cref="Dispatcher"/>.</summary>
        public static IDispatcher DefaultDispatcher { get; } = new InlineDispatcher((request, cancel) =>
            throw new DispatchException(DispatchErrorCode.ServiceNotFound, RetryPolicy.OtherReplica));

        /// <summary>Gets or sets the SSL client authentication options.</summary>
        /// <value>The SSL client authentication options. When not null, <see cref="Connection.ConnectAsync"/>
        /// will either establish a secure connection or fail.</value>
        public SslClientAuthenticationOptions? AuthenticationOptions { get; set; }

        /// <summary>Gets or sets the connection close timeout. This timeout is used when gracefully closing a
        /// connection to wait for the peer connection closure. If the peer doesn't close its side of the connection
        /// within the timeout timeframe, the connection is forcefully closed.</summary>
        /// <value>The close timeout value. The default is 10s.</value>
        public TimeSpan CloseTimeout
        {
            get => _closeTimeout;
            set => _closeTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(CloseTimeout)}", nameof(value));
        }

        /// <summary>Gets or sets the connection establishment timeout.</summary>
        /// <value>The connection establishment timeout value. The default is 10s.</value>
        public TimeSpan ConnectTimeout
        {
            get => _connectTimeout;
            set => _connectTimeout = value != TimeSpan.Zero ? value :
                throw new ArgumentException($"0 is not a valid value for {nameof(ConnectTimeout)}", nameof(value));
        }

        /// <summary>Gets or sets the dispatcher that dispatches requests received by this connection.</summary>
        /// <value>The dispatcher that dispatches requests received by this connection.</value>
        public IDispatcher Dispatcher { get; set; } = DefaultDispatcher;

        /// <summary>Gets of sets the default features of the new connection.</summary>
        public IFeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Gets or sets the client options for the ice protocol.</summary>
        /// <value>The client options for the ice protocol.</value>
        public IceClientOptions? IceClientOptions { get; set; }

        /// <summary>Gets or sets the client options for the icerpc protocol.</summary>
        /// <value>The client options for the icerpc protocol.</value>
        public IceRpcClientOptions? IceRpcClientOptions { get; set; }

        /// <summary>Specifies if the connection can be resumed after being closed.</summary>
        /// <value>When <c>true</c>, the connection will be re-established by the next call to
        /// <see cref="Connection.ConnectAsync"/> or the next invocation. The <see cref="Connection.State"/> is always
        /// switched back to <see cref="ConnectionState.NotConnected"/> after the connection closure. When <c>false</c>,
        /// the <see cref="Connection.State"/> is <see cref="ConnectionState.Closed"/> once the connection is closed and
        /// the connection won't be resumed. The default value is <c>false</c>.</value>
        public bool IsResumable { get; set; }

        /// <summary>Gets or sets the connection's keep alive. If a connection is kept alive, the connection
        /// monitoring will send keep alive frames to ensure the peer doesn't close the connection in the period defined
        /// by its idle timeout. How often keep alive frames are sent depends on the peer's IdleTimeout configuration.
        /// </summary>
        /// <value><c>true</c>to enable connection keep alive. <c>false</c> to disable it. The default is <c>false</c>.
        /// </value>
        public bool KeepAlive { get; set; }

        /// <summary>Gets or sets the logger factory used to create loggers to log connection-related activities.
        /// </summary>
        public ILoggerFactory LoggerFactory { get; set; } = NullLoggerFactory.Instance;

        /// <summary>Gets or set an action that executes when the connection is closed.</summary>
        public Action<Connection, Exception>? OnClose { get; set; }

        /// <summary>Gets or sets the connection's remote endpoint.</summary>
        public Endpoint? RemoteEndpoint
        {
            get => _remoteEndpoint;
            set
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

        private TimeSpan _closeTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
        private Endpoint? _remoteEndpoint;
    }
}
