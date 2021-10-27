// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    internal sealed class LogListenerDecorator<T> : IListener<T> where T : INetworkConnection
    {
        private readonly IListener<T> _decoratee;
        private readonly LogNetworkConnectionDecoratorFactory<T> _logDecoratorFactory;
        private readonly ILogger _logger;

        Endpoint IListener.Endpoint => _decoratee.Endpoint;

        async Task<T> IListener<T>.AcceptAsync()
        {
            try
            {
                T connection = await _decoratee.AcceptAsync().ConfigureAwait(false);
                return _logDecoratorFactory(connection, isServer: true, _decoratee.Endpoint, _logger);
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

        internal LogListenerDecorator(
            IListener<T> decoratee,
            ILogger logger,
            LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory)
        {
            _decoratee = decoratee;
            _logDecoratorFactory = logDecoratorFactory;
            _logger = logger;
            _logger.LogListenerListening(_decoratee.Endpoint);
        }
    }
}
