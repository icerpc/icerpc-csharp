// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;
using System;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the UDP transport.</summary>
    public class UdpClientTransport : IClientTransport
    {
        /// <inheritdoc/>
        public MultiStreamConnection CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions options,
            ILogger logger)
        {
            if (remoteEndpoint is UdpEndpoint udpEndpoint)
            {
                // TODO: temporary
                return udpEndpoint.CreateClientConnection(options, logger);
            }
            else
            {
                throw new ArgumentException("endpoint is not a UDP endpoint", nameof(remoteEndpoint));
            }
        }
    }
}
