// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>A factory for server connections. It is usually implemented by an <see cref="Endpoint"/> class.
    /// This factory is provided by a transport that can only communicate with a single client (e.g. a serial
    /// based transport) or that can receive data from multiple clients with a single connection (e.g: UDP). Most
    /// transports provide instead an <see cref="IListenerFactory"/>.</summary>
    public interface IServerConnectionFactory
    {
        /// <summary>Accepts a new connection from a client and returns a new server connection.</summary>
        /// <param name="options">The connection options.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The new connection. This connection is not yet connected.</returns>
        MultiStreamConnection Accept(ServerConnectionOptions options, ILogger logger);
    }
}
