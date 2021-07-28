// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> using other client transport implementations.</summary>
    public class CompositeClientTransport : Dictionary<string, IClientTransport>, IClientTransport
    {
        /// <inheritdoc/>
        public MultiStreamConnection CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory)
        {
            if (TryGetValue(remoteEndpoint.Transport, out IClientTransport? clientTransport))
            {
                return clientTransport.CreateConnection(remoteEndpoint, connectionOptions, loggerFactory);
            }
            else
            {
                throw new UnknownTransportException(remoteEndpoint.Transport);
            }
        }
    }
}
