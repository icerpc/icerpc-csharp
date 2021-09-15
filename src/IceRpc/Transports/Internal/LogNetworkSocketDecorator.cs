// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace IceRpc.Transports.Internal
{
    /// <summary>The LogNetworkSocketDecorator is a NetworkSocket decorator to log network socket calls.</summary>
    internal class LogNetworkSocketDecorator : INetworkSocket
    {
        /// <inheritdoc/>
        public virtual int DatagramMaxReceiveSize => _decoratee.DatagramMaxReceiveSize;

        /// <inheritdoc/>
        public bool IsDatagram => _decoratee.IsDatagram;

        /// <inheritdoc/>
        public Socket Socket => _decoratee.Socket;

        /// <inheritdoc/>
        public SslStream? SslStream => _decoratee.SslStream;

        private readonly INetworkSocket _decoratee;
        private readonly ILogger _logger;

        public async ValueTask<Endpoint> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
        {
            try
            {
                Endpoint result = await _decoratee.ConnectAsync(endpoint, cancel).ConfigureAwait(false);
                if (_decoratee.SslStream != null)
                {
                    _logger.LogTlsAuthenticationSucceeded(_decoratee.SslStream);
                }
                return result;
            }
            catch (TransportException exception) when (exception.InnerException is AuthenticationException ex)
            {
                _logger.LogTlsAuthenticationFailed(ex);
                throw;
            }
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await _decoratee.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Math.Min(buffer.Length, 32); ++i)
                {
                    sb.Append($"0x{buffer.Span[i]:X2} ");
                }
                if (buffer.Length > 32)
                {
                    sb.Append("...");
                }
                _logger.LogReceivedData(buffer.Length, sb.ToString().Trim());
            }
            return received;
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            await _decoratee.SendAsync(buffer, cancel).ConfigureAwait(false);

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var sb = new StringBuilder();
                if (buffer.Length < 32)
                {
                    for (int j = 0; j < Math.Min(buffer.Length, 32); ++j)
                    {
                        sb.Append($"0x{buffer.Span[j]:X2} ");
                    }
                }
                if (buffer.Length > 32)
                {
                    sb.Append("...");
                }
                _logger.LogSentData(buffer.Length, sb.ToString().Trim());
            }
        }

        public async ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            await _decoratee.SendAsync(buffers, cancel).ConfigureAwait(false);

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var sb = new StringBuilder();
                int size = 0;
                for (int i = 0; i < buffers.Length; ++i)
                {
                    ReadOnlyMemory<byte> buffer = buffers.Span[i];
                    if (size < 32)
                    {
                        for (int j = 0; j < Math.Min(buffer.Length, 32 - size); ++j)
                        {
                            sb.Append($"0x{buffer.Span[j]:X2} ");
                        }
                    }
                    size += buffer.Length;
                    if (size == 32 && i != buffers.Length)
                    {
                        sb.Append("...");
                    }
                }
                _logger.LogSentData(size, sb.ToString().Trim());
            }
        }

        internal LogNetworkSocketDecorator(INetworkSocket decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
