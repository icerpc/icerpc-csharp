// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>The log decorator installed by the UDP transports.</summary>
    internal class LogUdpNetworkConnectionDecorator : ISimpleNetworkConnection
    {
        bool INetworkConnection.IsSecure => _decoratee.IsSecure;
        TimeSpan INetworkConnection.LastActivity => _decoratee.LastActivity;

        private readonly ILogger _logger;
        private readonly UdpNetworkConnection _decoratee;

        void IDisposable.Dispose() => _decoratee.Dispose();

        async Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            (ISimpleStream, NetworkConnectionInformation) result =
                await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

            _logger.LogUdpConnect(_decoratee.Socket.ReceiveBufferSize, _decoratee.Socket.SendBufferSize);

            return result;
        }

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        public override string? ToString() => _decoratee.ToString();

        internal LogUdpNetworkConnectionDecorator(UdpNetworkConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
