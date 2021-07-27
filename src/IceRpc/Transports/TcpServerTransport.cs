// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;
using System;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the tcp and ssl transports.</summary>
    public class TcpServerTransport : IServerTransport
    {
        /// <inheritdoc/>
        public (IListener?, MultiStreamConnection?) Listen(
            Endpoint endpoint,
            ServerConnectionOptions options,
            ILogger logger)
        {
            if (endpoint is TcpEndpoint tcpEndpoint)
            {
                // TODO: temporary
                return (tcpEndpoint.CreateListener(options, logger), null);
            }
            else
            {
                throw new ArgumentException("endpoint is not a TCP endpoint", nameof(endpoint));
            }
        }
    }
}
