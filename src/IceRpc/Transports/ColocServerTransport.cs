// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

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
