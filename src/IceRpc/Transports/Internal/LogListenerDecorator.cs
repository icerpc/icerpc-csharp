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
                return _logDecoratorFactory(connection, _decoratee.Endpoint, isServer: true, _logger);
            }
            catch (ObjectDisposedException)
            {
                // We assume the decoratee is shut down which should not result in an error message.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogListenerAcceptFailed(_decoratee.Endpoint, ex);
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
                _logger.LogListenerDispose(_decoratee.Endpoint);
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
            _logger.LogListenerCreated(_decoratee.Endpoint);
        }
    }
}
