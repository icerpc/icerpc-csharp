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

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _decoratee.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _logger.LogListenerShutDown(_decoratee.Endpoint);
            }
        }

        public override string? ToString() => _decoratee.ToString();

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
