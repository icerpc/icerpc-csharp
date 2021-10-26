// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;

namespace IceRpc.Transports.Internal
{
    /// <summary>The log decorator installed by the TCP transports.</summary>
    internal class LogTcpNetworkConnectionDecorator : ISimpleNetworkConnection
    {
        bool INetworkConnection.IsSecure => _decoratee.IsSecure;
        TimeSpan INetworkConnection.LastActivity => _decoratee.LastActivity;

        private readonly ISimpleNetworkConnection _decoratee;

        private readonly ILogger _logger;
        private readonly TcpNetworkConnection _tcpConnection;

        void INetworkConnection.Close(Exception? exception) => _decoratee.Close(exception);

        async Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            try
            {
                var result = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

                if (_tcpConnection.SslStream is SslStream sslStream)
                {
                    _logger.LogTlsAuthenticationSucceeded(sslStream);
                }
                return result;
            }
            catch (TransportException exception) when (exception.InnerException is AuthenticationException ex)
            {
                _logger.LogTlsAuthenticationFailed(ex);
                throw;
            }
        }

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        internal LogTcpNetworkConnectionDecorator(TcpClientNetworkConnection clientConnection, ILogger logger)
        {
            _decoratee = clientConnection;
            _logger = logger;
            _tcpConnection = clientConnection;
        }

        internal LogTcpNetworkConnectionDecorator(TcpServerNetworkConnection serverConnection, ILogger logger)
        {
            _decoratee = serverConnection;
            _logger = logger;
            _tcpConnection = serverConnection;
        }
    }
}
