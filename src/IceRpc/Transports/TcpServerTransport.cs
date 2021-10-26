// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{ISimpleNetworkConnection}"/> for the tcp and ssl transports.
    /// </summary>
    public class TcpServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        private readonly SslServerAuthenticationOptions? _authenticationOptions;
        private readonly TcpOptions _tcpOptions;

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        public TcpServerTransport() :
            this(tcpOptions: new(), null)
        {
        }

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        /// <param name="authenticationOptions">The ssl authentication options. If not set, ssl is disabled.</param>
        public TcpServerTransport(SslServerAuthenticationOptions authenticationOptions) :
            this(tcpOptions: new(), authenticationOptions)
        {
        }

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        /// <param name="tcpOptions">The TCP transport options.</param>
        /// <param name="authenticationOptions">The ssl authentication options. If not set, ssl is disabled.</param>
        public TcpServerTransport(
            TcpOptions tcpOptions,
            SslServerAuthenticationOptions? authenticationOptions)
        {
            _tcpOptions = tcpOptions;
            _authenticationOptions = authenticationOptions;
        }

        /// <inheritdoc/>
        IListener<ISimpleNetworkConnection> IServerTransport<ISimpleNetworkConnection>.Listen(
            Endpoint endpoint,
            ILoggerFactory loggerFactory)
        {
            // This is the composition root of the tcp client transport, where we install log decorators when logging
            // is enabled.

            Func<TcpServerNetworkConnection, ISimpleNetworkConnection> serverConnectionDecorator =
                loggerFactory.CreateLogger("IceRpc.Transports") is ILogger logger && logger.IsEnabled(LogLevel.Error) ?
                    connection => new LogTcpNetworkConnectionDecorator(connection, logger) : connection => connection;

            return new TcpListener(endpoint, _tcpOptions, _authenticationOptions, serverConnectionDecorator);
        }
    }
}
