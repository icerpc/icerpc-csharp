// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>The log decorator installed by the UDP transports.</summary>
    internal class LogUdpNetworkConnectionDecorator : ISimpleNetworkConnection
    {
        bool INetworkConnection.IsSecure => Decoratee.IsSecure;
        TimeSpan INetworkConnection.LastActivity => Decoratee.LastActivity;

        private ISimpleNetworkConnection Decoratee => _udpNetworkConnection;

        private readonly Action<ILogger, int, int> _logSuccess;
        private readonly ILogger _logger;
        private readonly UdpNetworkConnection _udpNetworkConnection;

        void INetworkConnection.Close(Exception? exception) => Decoratee.Close(exception);

        async Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            (ISimpleStream, NetworkConnectionInformation) result =
                await Decoratee.ConnectAsync(cancel).ConfigureAwait(false);

            _logSuccess(_logger,
                        _udpNetworkConnection.Socket.ReceiveBufferSize,
                        _udpNetworkConnection.Socket.SendBufferSize);

            return result;
        }

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            Decoratee.HasCompatibleParams(remoteEndpoint);

        public override string? ToString() => Decoratee.ToString();

        internal LogUdpNetworkConnectionDecorator(UdpServerNetworkConnection udpServerNetworkConnection, ILogger logger)
            : this(udpServerNetworkConnection, logger, server: true)
        {
        }

        internal LogUdpNetworkConnectionDecorator(UdpClientNetworkConnection udpClientNetworkConnection, ILogger logger)
            : this(udpClientNetworkConnection, logger, server: false)
        {
        }

        private LogUdpNetworkConnectionDecorator(UdpNetworkConnection udpNetworkConnection, ILogger logger, bool server)
        {
            _logger = logger;
            _logSuccess = server ? TransportLoggerExtensions.LogUdpStartReceivingDatagrams :
                TransportLoggerExtensions.LogUdpStartSendingDatagrams;
            _udpNetworkConnection = udpNetworkConnection;
        }
    }
}
