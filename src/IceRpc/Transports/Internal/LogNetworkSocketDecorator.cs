// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace IceRpc.Transports.Internal
{
    /// <summary>The LogNetworkConnectionDecorator is a NetworkSocket decorator to log network socket calls.</summary>
    /// TODO: XXX: add scope support
    internal sealed class LogNetworkConnectionDecorator :
        INetworkConnection,
        ISingleStreamConnection,
        IMultiStreamConnection
    {
        /// <inheritdoc/>
        int INetworkConnection.DatagramMaxReceiveSize => _decoratee.DatagramMaxReceiveSize;

        /// <inheritdoc/>
        bool INetworkConnection.IsDatagram => _decoratee.IsDatagram;

        bool INetworkConnection.IsSecure => throw new NotImplementedException();

        bool INetworkConnection.IsServer => throw new NotImplementedException();

        Endpoint? INetworkConnection.LocalEndpoint => throw new NotImplementedException();

        Endpoint? INetworkConnection.RemoteEndpoint => throw new NotImplementedException();

        private readonly INetworkConnection _decoratee;
        private ISingleStreamConnection? _singleStreamDecoratee;
        private IMultiStreamConnection? _multiStreamDecoratee;
        private readonly ILogger _logger;

        public static INetworkConnection Create(INetworkConnection networkConnection, ILogger logger)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                return new LogNetworkConnectionDecorator(networkConnection, logger);
            }
            else
            {
                return networkConnection;
            }
        }

        async ValueTask<INetworkStream> IMultiStreamConnection.AcceptStreamAsync(CancellationToken cancel) =>
            new LogNetworkStreamDecorator(
                await _multiStreamDecoratee!.AcceptStreamAsync(cancel).ConfigureAwait(false),
                _logger);

        async ValueTask INetworkConnection.ConnectAsync(CancellationToken cancel)
        {
            try
            {
                await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
                if (_decoratee is NetworkSocketConnection connection &&
                    connection.NetworkSocket.SslStream is SslStream sslStream)
                {
                    _logger.LogTlsAuthenticationSucceeded(sslStream);
                }
            }
            catch (TransportException exception) when (exception.InnerException is AuthenticationException ex)
            {
                _logger.LogTlsAuthenticationFailed(ex);
                throw;
            }
        }

        INetworkStream IMultiStreamConnection.CreateStream(bool bidirectional) =>
            new LogNetworkStreamDecorator(_multiStreamDecoratee!.CreateStream(bidirectional), _logger);

        public void Dispose() => _decoratee.Dispose();

        ISingleStreamConnection INetworkConnection.GetSingleStreamConnection()
        {
            _singleStreamDecoratee = _decoratee.GetSingleStreamConnection();
            return this;
        }

        IMultiStreamConnection INetworkConnection.GetMultiStreamConnection()
        {
            _multiStreamDecoratee = _decoratee.GetMultiStreamConnection();
            return this;
        }

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        ValueTask IMultiStreamConnection.InitializeAsync(CancellationToken cancel) =>
            _multiStreamDecoratee!.InitializeAsync(cancel);

        async ValueTask<int> ISingleStreamConnection.ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await _singleStreamDecoratee!.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogReceivedData(buffer.Length, PrintReceivedData(buffer[0..received]));
            }
            return received;
        }

        async ValueTask ISingleStreamConnection.SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            await _singleStreamDecoratee!.SendAsync(buffer, cancel).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogSentData(buffer.Length, PrintSentData(buffer));
            }
        }

        async ValueTask ISingleStreamConnection.SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            await _singleStreamDecoratee!.SendAsync(buffers, cancel).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                (int sent, string data) = PrintSentData(buffers);
                _logger.LogSentData(sent, data);
            }
        }

        internal LogNetworkConnectionDecorator(INetworkConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }

        internal static string PrintReceivedData(ReadOnlyMemory<byte> buffer)
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
            return sb.ToString();
        }

        internal static string PrintSentData(ReadOnlyMemory<byte> buffer)
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
            return sb.ToString();
        }

        internal static (int, string) PrintSentData(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            int size = 0;
            var sb = new StringBuilder();
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
            return (size, sb.ToString());
        }
    }

    internal sealed class LogNetworkStreamDecorator : INetworkStream
    {
        private readonly INetworkStream _decoratee;
        private readonly ILogger _logger;

        public long Id => _decoratee.Id;
        public bool IsBidirectional => _decoratee.IsBidirectional;

        public void AbortRead(StreamError errorCode) => _decoratee.AbortRead(errorCode);

        public void AbortWrite(StreamError errorCode) => _decoratee.AbortWrite(errorCode);

        public Stream AsByteStream() => _decoratee.AsByteStream();

        public void EnableReceiveFlowControl() => _decoratee.EnableReceiveFlowControl();

        public void EnableSendFlowControl() => _decoratee.EnableSendFlowControl();

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await _decoratee.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogReceivedData(
                    buffer.Length,
                    LogNetworkConnectionDecorator.PrintReceivedData(buffer[0..received]));
            }
            return received;
        }

        public async ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            await _decoratee.SendAsync(buffers, endStream, cancel).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                (int sent, string data) = LogNetworkConnectionDecorator.PrintSentData(buffers);
                _logger.LogSentData(sent, data);
            }
        }

        public override string? ToString() => _decoratee.ToString();

        internal LogNetworkStreamDecorator(INetworkStream decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }
}
