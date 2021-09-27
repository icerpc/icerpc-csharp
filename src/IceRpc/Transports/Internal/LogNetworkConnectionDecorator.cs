// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
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
        int INetworkConnection.DatagramMaxReceiveSize => _decoratee.DatagramMaxReceiveSize;
        public TimeSpan IdleTimeout => _decoratee.IdleTimeout;
        bool INetworkConnection.IsDatagram => _decoratee.IsDatagram;
        bool INetworkConnection.IsSecure => _decoratee.IsSecure;
        bool INetworkConnection.IsServer => _decoratee.IsServer;
        public TimeSpan LastActivity => _decoratee.LastActivity;
        Endpoint? INetworkConnection.LocalEndpoint => _decoratee.LocalEndpoint;
        Endpoint? INetworkConnection.RemoteEndpoint => _decoratee.RemoteEndpoint;

        internal Exception? FailureException { get; set; }
        internal ILogger Logger { get; }

        private bool _connected;
        private readonly INetworkConnection _decoratee;
        private ISingleStreamConnection? _singleStreamDecoratee;
        private IMultiStreamConnection? _multiStreamDecoratee;

        async ValueTask<INetworkStream> IMultiStreamConnection.AcceptStreamAsync(CancellationToken cancel)
        {
            try
            {
                INetworkStream stream = await _multiStreamDecoratee!.AcceptStreamAsync(cancel).ConfigureAwait(false);
                return new LogNetworkStreamDecorator(stream, this);
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        async ValueTask INetworkConnection.ConnectAsync(CancellationToken cancel)
        {
            try
            {
                await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
                if (_decoratee is NetworkSocketConnection connection &&
                    connection.NetworkSocket.SslStream is SslStream sslStream)
                {
                    Logger.LogTlsAuthenticationSucceeded(sslStream);
                }
                Action logSuccess = (_decoratee.IsServer, _decoratee.IsDatagram) switch
                {
                    (false, false) => Logger.LogConnectionEstablished,
                    (false, true) => Logger.LogStartSendingDatagrams,
                    (true, false) => Logger.LogConnectionAccepted,
                    (true, true) => Logger.LogStartReceivingDatagrams
                };
                logSuccess();
                _connected = true;
            }
            catch (TransportException exception) when (exception.InnerException is AuthenticationException ex)
            {
                Logger.LogTlsAuthenticationFailed(ex);
                FailureException = exception;
                throw;
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        INetworkStream IMultiStreamConnection.CreateStream(bool bidirectional) =>
            new LogNetworkStreamDecorator(_multiStreamDecoratee!.CreateStream(bidirectional), this);

        public void Dispose()
        {
            if (_connected)
            {
                if (_decoratee.IsDatagram && _decoratee.IsServer)
                {
                    Logger.LogStopReceivingDatagrams();
                }
                else
                {
                    Logger.LogConnectionClosed(FailureException?.Message ?? "graceful close");
                }
            }
            else
            {
                // If the connection is connecting but not active yet, we print a trace to show that
                // the connection got connected or accepted before printing out the connection closed
                // trace.
                Action<Exception?> logFailure = (_decoratee.IsServer, _decoratee.IsDatagram) switch
                {
                    (false, false) => Logger.LogConnectionConnectFailed,
                    (false, true) => Logger.LogStartSendingDatagramsFailed,
                    (true, false) => Logger.LogConnectionAcceptFailed,
                    (true, true) => Logger.LogStartReceivingDatagramsFailed,
                };
                logFailure(FailureException);
            }

            _decoratee.Dispose();
        }

        async ValueTask<ISingleStreamConnection> INetworkConnection.GetSingleStreamConnectionAsync(
            CancellationToken cancel)
        {
            _singleStreamDecoratee = await _decoratee.GetSingleStreamConnectionAsync(cancel).ConfigureAwait(false);
            return this;
        }

        async ValueTask<IMultiStreamConnection> INetworkConnection.GetMultiStreamConnectionAsync(
            CancellationToken cancel)
        {
            _multiStreamDecoratee = await _decoratee.GetMultiStreamConnectionAsync(cancel).ConfigureAwait(false);
            return this;
        }

        bool INetworkConnection.HasCompatibleParams(Endpoint remoteEndpoint) =>
            _decoratee.HasCompatibleParams(remoteEndpoint);

        async ValueTask<int> ISingleStreamConnection.ReceiveAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                int received = await _singleStreamDecoratee!.ReceiveAsync(buffer, cancel).ConfigureAwait(false);
                if (Logger.IsEnabled(LogLevel.Trace))
                {
                    Logger.LogReceivedData(received, PrintReceivedData(buffer[0..received]));
                }
                return received;
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        async ValueTask ISingleStreamConnection.SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            try
            {
                await _singleStreamDecoratee!.SendAsync(buffer, cancel).ConfigureAwait(false);
                if (Logger.IsEnabled(LogLevel.Trace))
                {
                    Logger.LogSentData(buffer.Length, PrintSentData(buffer));
                }
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        async ValueTask ISingleStreamConnection.SendAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            CancellationToken cancel)
        {
            try
            {
                await _singleStreamDecoratee!.SendAsync(buffers, cancel).ConfigureAwait(false);
                if (Logger.IsEnabled(LogLevel.Trace))
                {
                    (int sent, string data) = PrintSentData(buffers);
                    Logger.LogSentData(sent, data);
                }
            }
            catch (Exception exception)
            {
                FailureException = exception;
                throw;
            }
        }

        internal LogNetworkConnectionDecorator(INetworkConnection decoratee, ILogger logger)
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
            return sb.ToString();
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
                        _ = sb.Append($"0x{buffer.Span[j]:X2} ");
                    }
                }
                size += buffer.Length;
                if (size == 32 && i != buffers.Length)
                {
                    _ = sb.Append("...");
                }
            }
            return (size, sb.ToString());
        }
    }

    internal sealed class LogNetworkStreamDecorator : INetworkStream
    {
        private readonly INetworkStream _decoratee;
        private readonly LogNetworkConnectionDecorator _parent;

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

        internal LogNetworkStreamDecorator(INetworkStream decoratee, LogNetworkConnectionDecorator parent)
        {
            _decoratee = decoratee;
            _parent = parent;
        }
    }
}
