// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;
using System;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the udp transport.</summary>
    public class UdpServerTransport : IServerTransport
    {
        /// <inheritdoc/>
        public (IListener?, MultiStreamConnection?) Listen(
            Endpoint endpoint,
            ServerConnectionOptions options,
            ILogger logger)
        {
            if (endpoint is UdpEndpoint udpEndpoint)
            {
                // TODO: temporary
                return (null, udpEndpoint.Accept(options, logger));
            }
            else
            {
                throw new ArgumentException("endpoint is not a UDP endpoint", nameof(endpoint));
            }
        }
    }
}
