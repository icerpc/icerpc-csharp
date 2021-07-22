// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> using other client transport implementations.</summary>
    public class CompositeClientTransport : Dictionary<Transport, IClientTransport>, IClientTransport
    {
        /// <inheritdoc/>
        public MultiStreamConnection CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions options,
            ILogger logger)
        {
            if (TryGetValue(remoteEndpoint.Transport, out IClientTransport? clientTransport))
            {
                return clientTransport.CreateConnection(remoteEndpoint, options, logger);
            }
            else
            {
                throw new UnknownTransportException(remoteEndpoint.Transport);
            }
        }
    }
}
