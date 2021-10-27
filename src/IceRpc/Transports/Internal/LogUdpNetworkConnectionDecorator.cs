// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>The log decorator installed by the UDP transports.</summary>
    internal class LogUdpNetworkConnectionDecorator : ISimpleNetworkConnection
    {
        bool INetworkConnection.IsSecure => _decoratee.IsSecure;
        TimeSpan INetworkConnection.LastActivity => _decoratee.LastActivity;

        private readonly Action<ILogger, int, int> _logSuccess;
        private readonly ILogger _logger;
        private readonly UdpNetworkConnection _decoratee;

        void INetworkConnection.Close(Exception? exception) => _decoratee.Close(exception);

        async Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            (ISimpleStream, NetworkConnectionInformation) result =
                await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

            _logSuccess(_logger,
                        _decoratee.Socket.ReceiveBufferSize,
                        _decoratee.Socket.SendBufferSize);

            return result;
        }

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        public override string? ToString() => _decoratee.ToString();

        internal LogUdpNetworkConnectionDecorator(UdpServerNetworkConnection udpServerNetworkConnection, ILogger logger)
            : this(udpServerNetworkConnection, logger, server: true)
        {
        }

        internal LogUdpNetworkConnectionDecorator(UdpClientNetworkConnection udpClientNetworkConnection, ILogger logger)
            : this(udpClientNetworkConnection, logger, server: false)
        {
        }

        private LogUdpNetworkConnectionDecorator(UdpNetworkConnection decoratee, ILogger logger, bool server)
        {
            _decoratee = decoratee;
            _logger = logger;
            _logSuccess = server ? TransportLoggerExtensions.LogUdpStartReceivingDatagrams :
                TransportLoggerExtensions.LogUdpStartSendingDatagrams;
        }
    }
}
