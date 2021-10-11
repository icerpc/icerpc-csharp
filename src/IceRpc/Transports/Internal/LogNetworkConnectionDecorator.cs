// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal class LogNetworkConnectionDecorator : INetworkConnection
    {
        public TimeSpan IdleTimeout => _decoratee.IdleTimeout;
        public bool IsSecure => _decoratee.IsSecure;
        public bool IsServer => _decoratee.IsServer;
        public TimeSpan LastActivity => _decoratee.LastActivity;
        public Endpoint? LocalEndpoint => _decoratee.LocalEndpoint;
        public ILogger Logger => _decoratee.Logger;
        public Endpoint? RemoteEndpoint => _decoratee.RemoteEndpoint;

        private bool _connected;
        private bool _isDatagram;
        private readonly INetworkConnection _decoratee;

        public void Close(Exception? exception)
        {
            using IDisposable? scope = Logger.StartConnectionScope(this);
            if (_connected || exception == null)
            {
                if (_isDatagram && _decoratee.IsServer)
                {
                    Logger.LogStopReceivingDatagrams();
                }
                else
                {
                    Logger.LogConnectionClosed(exception?.Message ?? "graceful close");
                }
            }
            else
            {
                // If the connection is connecting but not active yet, we print a trace to show that
                // the connection got connected or accepted before printing out the connection closed
                // trace.
                Action<Exception?> logFailure = (_decoratee.IsServer, _isDatagram) switch
                {
                    (false, false) => Logger.LogConnectionConnectFailed,
                    (false, true) => Logger.LogStartSendingDatagramsFailed,
                    (true, false) => Logger.LogConnectionAcceptFailed,
                    (true, true) => Logger.LogStartReceivingDatagramsFailed,
                };
                logFailure(exception);
            }
            _decoratee.Close(exception);
        }

        public virtual async ValueTask<ISingleStreamConnection> GetSingleStreamConnectionAsync(CancellationToken cancel)
        {
            ISingleStreamConnection singleStreamConnection = new LogSingleStreamConnectionDecorator(
                await _decoratee.GetSingleStreamConnectionAsync(cancel).ConfigureAwait(false),
                Logger);
            _isDatagram = singleStreamConnection.IsDatagram;
            LogConnected();
            return singleStreamConnection;
        }

        public async ValueTask<IMultiStreamConnection> GetMultiStreamConnectionAsync(CancellationToken cancel)
        {
            IMultiStreamConnection multiStreamConnection = new LogMultiStreamConnectionDecorator(
                await _decoratee.GetMultiStreamConnectionAsync(cancel).ConfigureAwait(false),
                Logger);
            LogConnected();
            return multiStreamConnection;
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => _decoratee.HasCompatibleParams(remoteEndpoint);

        internal LogNetworkConnectionDecorator(INetworkConnection decoratee) => _decoratee = decoratee;

        internal static string PrintReceivedData(ReadOnlyMemory<byte> buffer)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(buffer.Length, 32); ++i)
            {
                _ = sb.Append($"0x{buffer.Span[i]:X2} ");
            }
            if (buffer.Length > 32)
            {
                _ = sb.Append("...");
            }
            return sb.ToString().Trim();
        }

        internal static string PrintSentData(ReadOnlyMemory<byte> buffer)
        {
            var sb = new StringBuilder();
            if (buffer.Length < 32)
            {
                for (int j = 0; j < Math.Min(buffer.Length, 32); ++j)
                {
                    _ = sb.Append($"0x{buffer.Span[j]:X2} ");
                }
            }
            if (buffer.Length > 32)
            {
                _ = sb.Append("...");
            }
            return sb.ToString().Trim();
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
                        _ = sb.Append($"0x{buffer.Span[j]:X2} ");
                    }
                }
                size += buffer.Length;
                if (size == 32 && i != buffers.Length)
                {
                    _ = sb.Append("...");
                }
            }
            return (size, sb.ToString().Trim());
        }

        private void LogConnected()
        {
            if (!_connected)
            {
                using IDisposable? scope = Logger.StartConnectionScope(this);
                Action logSuccess = (_decoratee.IsServer, _isDatagram) switch
                {
                    (false, false) => Logger.LogConnectionEstablished,
                    (false, true) => Logger.LogStartSendingDatagrams,
                    (true, false) => Logger.LogConnectionAccepted,
                    (true, true) => Logger.LogStartReceivingDatagrams
                };
                logSuccess();
                _connected = true;
            }
        }
    }

    internal sealed class LogSingleStreamConnectionDecorator : ISingleStreamConnection
    {
        public int DatagramMaxReceiveSize => _decoratee.DatagramMaxReceiveSize;
        public bool IsDatagram => _decoratee.IsDatagram;

        private readonly ILogger _logger;
        private readonly ISingleStreamConnection _decoratee;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await _decoratee!.ReadAsync(buffer, cancel).ConfigureAwait(false);
            string data = LogNetworkConnectionDecorator.PrintReceivedData(buffer[0..received]);
            _logger.LogReceivedData(received, data);
            return received;
        }

        public async ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            await _decoratee!.WriteAsync(buffers, cancel).ConfigureAwait(false);
            (int sent, string data) = LogNetworkConnectionDecorator.PrintSentData(buffers);
            _logger.LogSentData(sent, data);
        }

        internal LogSingleStreamConnectionDecorator(ISingleStreamConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }

    internal sealed class LogMultiStreamConnectionDecorator : IMultiStreamConnection
    {
        private readonly ILogger _logger;
        private readonly IMultiStreamConnection _decoratee;

        async ValueTask<INetworkStream> IMultiStreamConnection.AcceptStreamAsync(CancellationToken cancel) =>
            new LogNetworkStreamDecorator(await _decoratee.AcceptStreamAsync(cancel).ConfigureAwait(false), _logger);

        INetworkStream IMultiStreamConnection.CreateStream(bool bidirectional) =>
            new LogNetworkStreamDecorator(_decoratee.CreateStream(bidirectional), _logger);

        internal LogMultiStreamConnectionDecorator(IMultiStreamConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }

    internal sealed class LogNetworkStreamDecorator : INetworkStream
    {
        public long Id => _decoratee.Id;
        public bool IsBidirectional => _decoratee.IsBidirectional;
        public Action? ShutdownAction
        {
            get => _decoratee.ShutdownAction;
            set => _decoratee.ShutdownAction = value;
        }

        private readonly INetworkStream _decoratee;
        private readonly ILogger _logger;

        public ReadOnlyMemory<byte> TransportHeader => _decoratee.TransportHeader;

        public void AbortRead(StreamError errorCode) => _decoratee.AbortRead(errorCode);

        public void AbortWrite(StreamError errorCode) => _decoratee.AbortWrite(errorCode);

        public Stream AsByteStream() => _decoratee.AsByteStream();

        public void EnableReceiveFlowControl() => _decoratee.EnableReceiveFlowControl();

        public void EnableSendFlowControl() => _decoratee.EnableSendFlowControl();

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await _decoratee.ReadAsync(buffer, cancel).ConfigureAwait(false);
            string data = LogNetworkConnectionDecorator.PrintReceivedData(buffer[0..received]);
            _logger.LogReceivedData(received, data);
            return received;
        }

        public async ValueTask WriteAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            await _decoratee.WriteAsync(buffers, endStream, cancel).ConfigureAwait(false);
            (int sent, string data) = LogNetworkConnectionDecorator.PrintSentData(buffers);
            _logger.LogSentData(sent, data);
        }

        public ValueTask ShutdownCompleted(CancellationToken cancel) => _decoratee.ShutdownCompleted(cancel);

        public override string? ToString() => _decoratee.ToString();

        internal LogNetworkStreamDecorator(INetworkStream decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }

    internal class LogNetworkSocketConnectionDecorator : LogNetworkConnectionDecorator
    {
        private readonly NetworkSocketConnection _decoratee;

        internal LogNetworkSocketConnectionDecorator(NetworkSocketConnection decoratee) :
            base(decoratee) => _decoratee = decoratee;

        public override async ValueTask<ISingleStreamConnection> GetSingleStreamConnectionAsync(
                CancellationToken cancel)
        {
            try
            {
                ISingleStreamConnection singleStreamConnection = new LogSingleStreamConnectionDecorator(
                    await _decoratee.GetSingleStreamConnectionAsync(cancel).ConfigureAwait(false),
                    Logger);

                if (_decoratee.NetworkSocket.SslStream is SslStream sslStream)
                {
                    Logger.LogTlsAuthenticationSucceeded(sslStream);
                }
                return singleStreamConnection;
            }
            catch (TransportException exception) when (exception.InnerException is AuthenticationException ex)
            {
                Logger.LogTlsAuthenticationFailed(ex);
                throw;
            }
        }
    }
}
