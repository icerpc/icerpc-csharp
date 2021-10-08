// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    internal sealed class LogListenerDecorator : IListener
    {
        private readonly IListener _decoratee;

        public Endpoint Endpoint => _decoratee.Endpoint;

        public async ValueTask<INetworkConnection> AcceptAsync()
        {
            INetworkConnection connection = await _decoratee.AcceptAsync().ConfigureAwait(false);
            return connection.Logger.IsEnabled(LogLevel.Trace) ?
                new LogNetworkConnectionDecorator(connection, isServer: true) :
                connection;
        }

        public void Dispose() => _decoratee.Dispose();

        internal LogListenerDecorator(IListener decoratee) => _decoratee = decoratee;
    }
}
