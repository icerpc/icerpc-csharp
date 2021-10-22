// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{ISimpleNetworkConnection}"/> for the coloc transport.</summary>
    public class ColocServerTransport : IServerTransport<ISimpleNetworkConnection>
    {
        /// <inheritdoc/>
        IListener<ISimpleNetworkConnection> IServerTransport<ISimpleNetworkConnection>.Listen(
            Endpoint endpoint,
            ILoggerFactory loggerFactory) =>
            new ColocListener(endpoint);
    }
}
