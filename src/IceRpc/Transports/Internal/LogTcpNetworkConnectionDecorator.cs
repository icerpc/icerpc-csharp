// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.IO.Pipelines;
using System.Security.Authentication;

namespace IceRpc.Transports.Internal
{
    /// <summary>The log decorator installed by the TCP transports.</summary>
    internal class LogTcpNetworkConnectionDecorator : ISimpleNetworkConnection
    {
        PipeReader IDuplexPipe.Input => _decoratee.Input;
        PipeWriter IDuplexPipe.Output => _decoratee.Output;

        bool INetworkConnection.IsSecure => _decoratee.IsSecure;
        TimeSpan INetworkConnection.LastActivity => _decoratee.LastActivity;

        private readonly TcpNetworkConnection _decoratee;
        private readonly ILogger _logger;

        ValueTask IAsyncDisposable.DisposeAsync() => _decoratee.DisposeAsync();

        public async Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel)
        {
            try
            {
                NetworkConnectionInformation result = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);

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

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel) =>
            _decoratee.ReadAsync(buffer, cancel);

        public override string? ToString() => _decoratee.ToString();

        public ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel) =>
            _decoratee.WriteAsync(buffers, cancel);

        internal LogTcpNetworkConnectionDecorator(TcpNetworkConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
