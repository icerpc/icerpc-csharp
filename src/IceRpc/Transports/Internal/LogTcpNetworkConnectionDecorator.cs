// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;

namespace IceRpc.Transports.Internal
{
    /// <summary>The log decorator installed by the TCP transports.</summary>
    internal class LogTcpNetworkConnectionDecorator : ISimpleNetworkConnection
    {
        bool INetworkConnection.IsSecure => Decoratee.IsSecure;
        TimeSpan INetworkConnection.LastActivity => Decoratee.LastActivity;

        private ISimpleNetworkConnection Decoratee => _tcpNetworkConnection;

        private readonly Action<int, int> _logSuccess;
        private readonly ILogger _logger;
        private readonly TcpNetworkConnection _tcpNetworkConnection;

        void INetworkConnection.Close(Exception? exception) => Decoratee.Close(exception);

        async Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            try
            {
                (ISimpleStream, NetworkConnectionInformation) result =
                    await Decoratee.ConnectAsync(cancel).ConfigureAwait(false);

                if (_tcpNetworkConnection.SslStream is SslStream sslStream)
                {
                    _logger.LogTlsAuthenticationSucceeded(sslStream);
                }

                _logSuccess(_tcpNetworkConnection.Socket.ReceiveBufferSize,
                            _tcpNetworkConnection.Socket.SendBufferSize);

                return result;
            }
            catch (TransportException exception) when (exception.InnerException is AuthenticationException ex)
            {
                _logger.LogTlsAuthenticationFailed(ex);
                throw;
            }
        }

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            Decoratee.HasCompatibleParams(remoteEndpoint);

        internal LogTcpNetworkConnectionDecorator(TcpServerNetworkConnection tcpServerNetworkConnection, ILogger logger)
            : this(tcpServerNetworkConnection, logger, server: true)
        {
        }

        internal LogTcpNetworkConnectionDecorator(TcpClientNetworkConnection tcpClientNetworkConnection, ILogger logger)
            : this(tcpClientNetworkConnection, logger, server: false)
        {
        }

        private LogTcpNetworkConnectionDecorator(TcpNetworkConnection tcpNetworkConnection, ILogger logger, bool server)
        {
            _logger = logger;
            _logSuccess = server ?
                _logger.LogTcpNetworkConnectionAccepted : logger.LogTcpNetworkConnectionEstablished;
            _tcpNetworkConnection = tcpNetworkConnection;
        }
    }
}
