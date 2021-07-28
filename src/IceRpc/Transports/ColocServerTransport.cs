// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the coloc transport.</summary>
    public class ColocServerTransport : IServerTransport
    {
        (IListener?, MultiStreamConnection?) IServerTransport.Listen(
            Endpoint endpoint,
            ServerConnectionOptions options,
            ILogger logger) => (new ColocListener(endpoint, options, logger), null);
    }
}
