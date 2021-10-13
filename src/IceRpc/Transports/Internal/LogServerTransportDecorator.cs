// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>Builds a log server transport decorator.</summary>
    internal class LogServerTransportDecorator : IServerTransport
    {
        private readonly IServerTransport _decoratee;
        private readonly ILogger _logger;

        /// <inheritdoc/>
        public IListener Listen(Endpoint endpoint) => new LogListenerDecorator(_decoratee.Listen(endpoint), _logger);

        /// <summary>Constructs a server transport decorator to log traces.</summary>
        /// <param name="decoratee">The server transport to decorate.</param>
        /// <param name="logger">The logger.</param>
        internal LogServerTransportDecorator(IServerTransport decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
