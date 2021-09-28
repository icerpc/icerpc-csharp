// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;

namespace IceRpc.Transports.Internal
{
    /// <summary>The LogNetworkConnectionDecorator is a NetworkSocket decorator to log network socket calls.</summary>
    /// TODO: XXX: add scope support
    internal sealed class LogNetworkConnectionDecorator : INetworkConnection
    {
        public int DatagramMaxReceiveSize => _decoratee.DatagramMaxReceiveSize;
        public TimeSpan IdleTimeout => _decoratee.IdleTimeout;
        public bool IsDatagram => _decoratee.IsDatagram;
        public bool IsSecure => _decoratee.IsSecure;
        public bool IsServer => _decoratee.IsServer;
        public TimeSpan LastActivity => _decoratee.LastActivity;
        public Endpoint? LocalEndpoint => _decoratee.LocalEndpoint;
        public Endpoint? RemoteEndpoint => _decoratee.RemoteEndpoint;

        internal Exception? FailureException { get; set; }

        private bool _connected;
        private readonly INetworkConnection _decoratee;
        private readonly ILogger _logger;

        public async ValueTask ConnectAsync(CancellationToken cancel)
        {
            try
            {
                await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
                if (_decoratee is NetworkSocketConnection connection &&
                    connection.NetworkSocket.SslStream is SslStream sslStream)
                {
                    _logger.LogTlsAuthenticationSucceeded(sslStream);
                }
                Action logSuccess = (_decoratee.IsServer, _decoratee.IsDatagram) switch
                {
                    (false, false) => _logger.LogConnectionEstablished,
                    (false, true) => _logger.LogStartSendingDatagrams,
                    (true, false) => _logger.LogConnectionAccepted,
                    (true, true) => _logger.LogStartReceivingDatagrams
                };
                logSuccess();
                _connected = true;
            }
            catch (TransportException exception) when (exception.InnerException is AuthenticationException ex)
            {
                _logger.LogTlsAuthenticationFailed(ex);
                FailureException = exception;
                throw;
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        public void Dispose()
        {
            if (_connected)
            {
                if (_decoratee.IsDatagram && _decoratee.IsServer)
                {
                    _logger.LogStopReceivingDatagrams();
                }
                else
                {
                    _logger.LogConnectionClosed(FailureException?.Message ?? "graceful close");
                }
            }
            else
            {
                // If the connection is connecting but not active yet, we print a trace to show that
                // the connection got connected or accepted before printing out the connection closed
                // trace.
                Action<Exception?> logFailure = (_decoratee.IsServer, _decoratee.IsDatagram) switch
                {
                    (false, false) => _logger.LogConnectionConnectFailed,
                    (false, true) => _logger.LogStartSendingDatagramsFailed,
                    (true, false) => _logger.LogConnectionAcceptFailed,
                    (true, true) => _logger.LogStartReceivingDatagramsFailed,
                };
                logFailure(FailureException);
            }

            _decoratee.Dispose();
        }

        public ValueTask<ISingleStreamConnection> GetSingleStreamConnectionAsync(CancellationToken cancel) =>
            _decoratee.GetSingleStreamConnectionAsync(cancel);

        public ValueTask<IMultiStreamConnection> GetMultiStreamConnectionAsync(CancellationToken cancel) =>
            _decoratee.GetMultiStreamConnectionAsync(cancel);

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

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
    }

    internal sealed class LogSingleStreamConnectionDecorator : ISingleStreamConnection
    {
        internal Exception? FailureException { get; set; }
        private readonly ILogger _logger;

        private readonly ISingleStreamConnection? _decoratee;

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                int received = await _decoratee!.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    string data = LogNetworkConnectionDecorator.PrintReceivedData(buffer[0..received]);
                    _logger.LogReceivedData(received, data);
                }
                return received;
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                await _decoratee!.SendAsync(buffer, cancel).ConfigureAwait(false);
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    string data = LogNetworkConnectionDecorator.PrintSentData(buffer);
                    _logger.LogSentData(buffer.Length, data);
                }
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        public async ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            try
            {
                await _decoratee!.SendAsync(buffers, cancel).ConfigureAwait(false);
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    (int sent, string data) = LogNetworkConnectionDecorator.PrintSentData(buffers);
                    _logger.LogSentData(sent, data);
                }
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        internal LogSingleStreamConnectionDecorator(ISingleStreamConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            _logger = logger;
        }
    }

    internal sealed class LogMultiStreamConnectionDecorator : IMultiStreamConnection
    {
        internal Exception? FailureException { get; set; }
        internal ILogger Logger { get; }

        private readonly IMultiStreamConnection? _decoratee;

        async ValueTask<INetworkStream> IMultiStreamConnection.AcceptStreamAsync(CancellationToken cancel)
        {
            try
            {
                INetworkStream stream = await _decoratee!.AcceptStreamAsync(cancel).ConfigureAwait(false);
                return new LogNetworkStreamDecorator(stream, this);
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        INetworkStream IMultiStreamConnection.CreateStream(bool bidirectional) =>
            new LogNetworkStreamDecorator(_decoratee!.CreateStream(bidirectional), this);

        internal LogMultiStreamConnectionDecorator(IMultiStreamConnection decoratee, ILogger logger)
        {
            _decoratee = decoratee;
            Logger = logger;
        }

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
    }

    internal sealed class LogNetworkStreamDecorator : INetworkStream
    {
        private readonly INetworkStream _decoratee;
        private readonly LogMultiStreamConnectionDecorator _parent;

        public long Id => _decoratee.Id;
        public bool IsBidirectional => _decoratee.IsBidirectional;
        public bool ReadsCompleted { get; }
        public Action? ShutdownAction
        {
            get => _decoratee.ShutdownAction;
            set => _decoratee.ShutdownAction = value;
        }
        public ReadOnlyMemory<byte> TransportHeader => _decoratee.TransportHeader;

        public void AbortRead(StreamError errorCode) => _decoratee.AbortRead(errorCode);

        public void AbortWrite(StreamError errorCode) => _decoratee.AbortWrite(errorCode);

        public Stream AsByteStream() => _decoratee.AsByteStream();

        public void EnableReceiveFlowControl() => _decoratee.EnableReceiveFlowControl();

        public void EnableSendFlowControl() => _decoratee.EnableSendFlowControl();

        public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                int received = await _decoratee.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                if (_parent.Logger.IsEnabled(LogLevel.Trace))
                {
                    string data = LogNetworkConnectionDecorator.PrintReceivedData(buffer[0..received]);
                    _parent.Logger.LogReceivedData(received, data);
                }
                return received;
            }
            catch (Exception exception)
            {
                _parent.FailureException = exception;
                throw;
            }
        }

        public async ValueTask SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            try
            {
                await _decoratee.SendAsync(buffers, endStream, cancel).ConfigureAwait(false);
                if (_parent.Logger.IsEnabled(LogLevel.Trace))
                {
                    (int sent, string data) = LogNetworkConnectionDecorator.PrintSentData(buffers);
                    _parent.Logger.LogSentData(sent, data);
                }
            }
            catch (Exception exception)
            {
                _parent.FailureException = exception;
                throw;
            }
        }

        public ValueTask ShutdownCompleted(CancellationToken cancel) => _decoratee.ShutdownCompleted(cancel);

        public override string? ToString() => _decoratee.ToString();

        internal LogNetworkStreamDecorator(INetworkStream decoratee, LogMultiStreamConnectionDecorator parent)
        {
            _decoratee = decoratee;
            _parent = parent;
        }
    }
}
