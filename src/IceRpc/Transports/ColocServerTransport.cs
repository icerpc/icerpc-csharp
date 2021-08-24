// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport"/> for the coloc transport.</summary>
    public class ColocServerTransport : IServerTransport
    {
        private readonly MultiStreamOptions _multiStreamOptions;

        (IListener?, MultiStreamConnection?) IServerTransport.Listen(Endpoint endpoint, ILoggerFactory loggerFactory) =>
            (new ColocListener(endpoint, _multiStreamOptions, loggerFactory.CreateLogger("IceRpc")), null);

        /// <summary>Construct a colocated server transport.</summary>
        /// <param name="options">The transport options.</param>
        public ColocServerTransport(MultiStreamOptions options) => _multiStreamOptions = options;
    }
}
