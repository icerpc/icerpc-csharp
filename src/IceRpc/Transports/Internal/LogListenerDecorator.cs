// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    internal sealed class LogListenerDecorator : IListener
    {
        private readonly IListener _decoratee;
        private readonly ILogger _logger;

        public Endpoint Endpoint => _decoratee.Endpoint;

        public async  Task<INetworkConnection> AcceptAsync()
        {
            try
            {
                INetworkConnection connection = await _decoratee.AcceptAsync().ConfigureAwait(false);
                if (connection is SocketNetworkConnection networkSocketConnection)
                {
                    return new LogNetworkSocketConnectionDecorator(
                        networkSocketConnection,
                        isServer: true,
                        _decoratee.Endpoint,
                        _logger);
                }
                else
                {
                    return new LogNetworkConnectionDecorator(connection, isServer: true, _decoratee.Endpoint, _logger);
                }
            }
            catch (Exception ex)
            {
                _logger.LogListenerAcceptingConnectionFailed(_decoratee.Endpoint, ex);
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                _decoratee.Dispose();
            }
            finally
            {
                _logger.LogListenerShutDown(_decoratee.Endpoint);
            }
        }

        internal LogListenerDecorator(IListener decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
            _logger.LogListenerListening(_decoratee.Endpoint);
        }
    }
}
