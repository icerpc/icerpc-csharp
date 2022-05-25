// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc
{
    /// <summary>Represents a client connection used to send and receive requests and responses.</summary>
    public sealed class ClientConnection : Connection
    {
        /// <summary>The default client transport for icerpc protocol connections.</summary>
        public static IClientTransport<IMultiplexedNetworkConnection> DefaultMultiplexedClientTransport { get; } =
            new SlicClientTransport(new TcpClientTransport());

        /// <summary>The default client transport for ice protocol connections.</summary>
        public static IClientTransport<ISimpleNetworkConnection> DefaultSimpleClientTransport { get; } =
            new TcpClientTransport();

        private readonly ClientConnectionOptions _options;
        private readonly IClientTransport<IMultiplexedNetworkConnection> _multiplexedClientTransport;
        private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;

        /// <summary>Constructs a client connection.</summary>
        /// <param name="options">The connection options.</param>
        /// <param name="loggerFactory">The logger factory used to create loggers to log connection-related activities.
        /// </param>
        /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol connections.
        /// </param>
        /// <param name="simpleClientTransport">The simple transport used to create ice protocol connections.</param>
        public ClientConnection(
            ClientConnectionOptions options,
            ILoggerFactory? loggerFactory = null,
            IClientTransport<IMultiplexedNetworkConnection>? multiplexedClientTransport = null,
            IClientTransport<ISimpleNetworkConnection>? simpleClientTransport = null) :
            base(
                options,
                options.IsResumable,
                options.RemoteEndpoint ?? throw new ArgumentException(
                    $"{nameof(ClientConnectionOptions.RemoteEndpoint)} is not set",
                    nameof(options)),
                loggerFactory)
        {
            // At this point, we consider options to be read-only.
            // TODO: replace _options by "splatted" properties.
            _options = options;
            _multiplexedClientTransport = multiplexedClientTransport ?? DefaultMultiplexedClientTransport;
            _simpleClientTransport = simpleClientTransport ?? DefaultSimpleClientTransport;
        }

        /// <summary>Constructs a client connection with the specified remote endpoint and  authentication options.
        /// All other properties have their default values.</summary>
        /// <param name="endpoint">The connection remote endpoint.</param>
        /// <param name="authenticationOptions">The client authentication options.</param>
        public ClientConnection(Endpoint endpoint, SslClientAuthenticationOptions? authenticationOptions = null)
            : this(new ClientConnectionOptions
            {
                ClientAuthenticationOptions = authenticationOptions,
                RemoteEndpoint = endpoint
            })
        {
        }

        /// <inheritdoc/>
        public override Task ConnectAsync(CancellationToken cancel = default) =>
            ConnectAsync(_multiplexedClientTransport, _simpleClientTransport, _options.ClientAuthenticationOptions, cancel);
    }
}
