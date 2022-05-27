// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    internal class ServerConnection : Connection
    {
        /// <summary>Constructs a server connection from an accepted network connection.</summary>
        internal ServerConnection(Endpoint endpoint, ConnectionOptions options, ILoggerFactory? loggerFactory)
            : base(endpoint, options, loggerFactory)
        {
        }

        /// <inheritdoc/>
        public override Task ConnectAsync(CancellationToken cancel = default) => Task.CompletedTask;
    }
}
