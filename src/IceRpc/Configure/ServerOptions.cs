// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Net.Security;

namespace IceRpc.Configure
{
    /// <summary>A property bag used to configure a <see cref="Server"/>.</summary>
    public sealed record class ServerOptions
    {
        /// <summary>Returns the default value for <see cref="MultiplexedServerTransport"/>.</summary>
        public static IServerTransport<IMultiplexedNetworkConnection> DefaultMultiplexedServerTransport { get; } =
            new CompositeMultiplexedServerTransport().UseSlicOverTcp();

        /// <summary>Returns the default value for <see cref="SimpleServerTransport"/>.</summary>
        public static IServerTransport<ISimpleNetworkConnection> DefaultSimpleServerTransport { get; } =
            new CompositeSimpleServerTransport().UseTcp().UseUdp();

        /// <summary>Gets or sets the SSL server authentication options.</summary>
        /// <value>The SSL server authentication options. When not null, the server will accept only secure connections.
        /// </value>
        public SslServerAuthenticationOptions? AuthenticationOptions { get; set; }

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

        /// <summary>Gets or sets the server's dispatcher.</summary>
        /// <seealso cref="IDispatcher"/>
        /// <seealso cref="Router"/>
        public IDispatcher Dispatcher { get; set; } = ConnectionOptions.DefaultDispatcher;

        /// <summary>Gets or sets the server's endpoint. The endpoint's host is usually an IP address, and it
        /// cannot be a DNS name.</summary>
        public Endpoint Endpoint
        {
            get => _endpoint;
            set => _endpoint = value.Protocol.IsSupported ? value :
                throw new NotSupportedException($"cannot set endpoint with protocol '{value.Protocol}'");
        }

        /// <summary>Gets or sets the connection fields to send to the clients.</summary>
        public IDictionary<ConnectionFieldKey, OutgoingFieldValue> Fields { get; set; } =
            ImmutableDictionary<ConnectionFieldKey, OutgoingFieldValue>.Empty;

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

        /// <summary>Gets or sets <see cref="IServerTransport{IMultiplexedNetworkConnection}"/> used by the
        /// server to accept multiplexed connections.</summary>
        public IServerTransport<IMultiplexedNetworkConnection> MultiplexedServerTransport { get; set; } =
            DefaultMultiplexedServerTransport;

        /// <summary>Gets or sets the <see cref="IServerTransport{ISimpleNetworkConnection}"/> used by the server
        /// to accept simple connections.</summary>
        public IServerTransport<ISimpleNetworkConnection> SimpleServerTransport { get; set; } =
            DefaultSimpleServerTransport;

        private TimeSpan _closeTimeout = TimeSpan.FromSeconds(10);
        private TimeSpan _connectTimeout = TimeSpan.FromSeconds(10);
        private Endpoint _endpoint = new(Protocol.IceRpc);
    }
}
