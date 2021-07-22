// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Gives Connections the ability to create outgoing transport connections.</summary>
    public interface IClientTransport
    {
        /// <summary>Creates a new multi-stream connection to the remote endpoint.</summary>
        /// <param name="remoteEndpoint">The remote endpoint.</param>
        /// <param name="options">The connection options.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The new connection. This connection is not yet connected.</returns>
        MultiStreamConnection CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions options,
            ILogger logger);
    }
}
