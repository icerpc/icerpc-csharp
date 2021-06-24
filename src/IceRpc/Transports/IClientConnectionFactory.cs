// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>A factory for client connections. It is usually implemented by an <see cref="Endpoint"/> class.
    /// </summary>
    public interface IClientConnectionFactory
    {
        /// <summary>Creates a new connection to the remote endpoint of this factory.</summary>
        /// <param name="options">The connection options.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The new connection. This connection is not yet connected.</returns>
        MultiStreamConnection CreateClientConnection(ClientConnectionOptions options, ILogger logger);
    }
}
