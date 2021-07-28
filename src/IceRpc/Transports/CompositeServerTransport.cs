// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> using other server transport implementations.</summary>
    public class CompositeServerTransport : Dictionary<string, IServerTransport>, IServerTransport
    {
        /// <inheritdoc/>
        public (IListener?, MultiStreamConnection?) Listen(
            Endpoint endpoint,
            ServerConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory)
        {
            if (TryGetValue(endpoint.Transport, out IServerTransport? serverTransport))
            {
                return serverTransport.Listen(endpoint, connectionOptions, loggerFactory);
            }
            else
            {
                throw new UnknownTransportException(endpoint.Transport);
            }
        }
    }
}
