// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;
using System;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the tcp and ssl transports.</summary>
    public class TcpClientTransport : IClientTransport
    {
        /// <inheritdoc/>
        public MultiStreamConnection CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions options,
            ILogger logger)
        {
            if (remoteEndpoint is TcpEndpoint tcpEndpoint)
            {
                // TODO: temporary
                return tcpEndpoint.CreateClientConnection(options, logger);
            }
            else
            {
                throw new ArgumentException("endpoint is not a TCP endpoint", nameof(remoteEndpoint));
            }
        }
    }
}
