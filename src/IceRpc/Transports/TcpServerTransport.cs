// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{ISimpleNetworkConnection}"/> for the tcp and ssl transports.
    /// </summary>
    public class TcpServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        /// <inheritdoc/>
        public string Name => TransportNames.Tcp;

        private readonly TcpServerTransportOptions _options;

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        public TcpServerTransport()
            : this(new())
        {
        }

        /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
        /// <param name="options">The transport options.</param>
        public TcpServerTransport(TcpServerTransportOptions options) => _options = options;

        /// <inheritdoc/>
        public IListener<ISimpleNetworkConnection> Listen(
            Endpoint endpoint,
            SslServerAuthenticationOptions? authenticationOptions,
            ILogger logger)
        {
            // This is the composition root of the tcp server transport, where we install log decorators when logging
            // is enabled.

            string? endpointTransport = endpoint.ParseTcpParams();

            if (endpointTransport == null)
            {
                endpoint = endpoint with { Params = endpoint.Params.Add("transport", Name) };
            }
            else if (endpointTransport == TransportNames.Ssl && authenticationOptions == null)
            {
                throw new ArgumentNullException(
                    nameof(authenticationOptions),
                    $"{nameof(authenticationOptions)} cannot be null with the ssl transport");
            }

            Func<TcpServerNetworkConnection, ISimpleNetworkConnection> serverConnectionDecorator =
                logger.IsEnabled(TcpLoggerExtensions.MaxLogLevel) ?
                    connection => new LogTcpNetworkConnectionDecorator(connection, logger) : connection => connection;

            return new TcpListener(endpoint, authenticationOptions, _options, serverConnectionDecorator);
        }
    }
}
