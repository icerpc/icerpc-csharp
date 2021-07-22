// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;
using System;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the coloc transport.</summary>
    public class ColocClientTransport : IClientTransport
    {
        /// <inheritdoc/>
        public MultiStreamConnection CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions options,
            ILogger logger)
        {
            if (remoteEndpoint is ColocEndpoint colocEndpoint)
            {
                // TODO: temporary
                return colocEndpoint.CreateClientConnection(options, logger);
            }
            else
            {
                throw new ArgumentException("endpoint is not a coloc endpoint", nameof(remoteEndpoint));
            }
        }
    }
}
