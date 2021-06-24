// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>A factory for <see cref="IListener"/>. It is usually implemented by an <see cref="Endpoint"/> class.
    /// </summary>
    public interface IListenerFactory
    {
        /// <summary>Creates a new listener that listens on the endpoint of this factory.</summary>
        /// <param name="options">The connection options that the listener uses when accepting new connections.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>The new listener.</returns>
        IListener CreateListener(ServerConnectionOptions options, ILogger logger);
    }
}
