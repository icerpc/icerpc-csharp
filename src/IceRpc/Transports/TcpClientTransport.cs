// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the tcp and ssl transports.
    /// </summary>
    public class TcpClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        private readonly SslClientAuthenticationOptions? _authenticationOptions;
        private readonly TcpOptions _tcpOptions;

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        public TcpClientTransport() :
            this(tcpOptions: new(), null)
        {
        }

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        public TcpClientTransport(SslClientAuthenticationOptions authenticationOptions) :
            this(tcpOptions: new(), authenticationOptions)
        {
        }

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options.</param>
        public TcpClientTransport(TcpOptions tcpOptions, SslClientAuthenticationOptions? authenticationOptions)
        {
            _tcpOptions = tcpOptions;
            _authenticationOptions = authenticationOptions;
        }

        /// <inheritdoc/>
        ISimpleNetworkConnection IClientTransport<ISimpleNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint,
            ILoggerFactory loggerFactory)
        {
            // This is the composition root of the tcp client transport, where we install log decorators when logging
            // is enabled.
            var clientConnection = new TcpClientNetworkConnection(remoteEndpoint, _tcpOptions, _authenticationOptions);

            return loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger &&
                logger.IsEnabled(TcpLoggerExtensions.MaxLogLevel) ?
                    new LogTcpNetworkConnectionDecorator(clientConnection, logger) : clientConnection;
        }
    }
}
