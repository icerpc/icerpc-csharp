// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;
using System;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the coloc transport.</summary>
    public class ColocServerTransport : IServerTransport
    {
        /// <inheritdoc/>
        public (IListener?, MultiStreamConnection?) Listen(
            Endpoint endpoint,
            ServerConnectionOptions options,
            ILogger logger)
        {
            if (endpoint is ColocEndpoint colocEndpoint)
            {
                // TODO: temporary
                return (colocEndpoint.CreateListener(options, logger), null);
            }
            else
            {
                throw new ArgumentException("endpoint is not a coloc endpoint", nameof(endpoint));
            }
        }
    }
}
