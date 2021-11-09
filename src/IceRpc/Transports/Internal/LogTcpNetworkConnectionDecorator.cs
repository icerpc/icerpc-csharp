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

        private readonly TcpNetworkConnection _decoratee;
        private readonly ILogger _logger;

        void IDisposable.Dispose() => _decoratee.Dispose();

        async Task<(ISimpleStream, NetworkConnectionInformation)> ISimpleNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            try
            {
                (ISimpleStream, NetworkConnectionInformation) result =
                    await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

                if (_decoratee.SslStream is SslStream sslStream)
                {
                    _logger.LogTlsAuthentication(sslStream);
                }

                _logger.LogTcpConnect(_decoratee.Socket.ReceiveBufferSize, _decoratee.Socket.SendBufferSize);

                return result;
            }
            catch (AuthenticationException ex)
            {
                _logger.LogTlsAuthenticationFailed(ex);
                throw;
            }
        }

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        public override string? ToString() => _decoratee.ToString();

        internal LogTcpNetworkConnectionDecorator(TcpNetworkConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
