// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal class LogNetworkConnectionDecorator : INetworkConnection
    {
        public TimeSpan IdleTimeout => Decoratee.IdleTimeout;
         public bool IsSecure => Decoratee.IsSecure;
        public TimeSpan LastActivity => Decoratee.LastActivity;
        public Endpoint? LocalEndpoint => Decoratee.LocalEndpoint;
        public Endpoint? RemoteEndpoint => Decoratee.RemoteEndpoint;
        // TODO: this property is need to support Connection.NetworkSocket. We should consider to remove
        // Connection.NetworkSocket instead.
        internal INetworkConnection Decoratee { get; }
        private protected ILogger Logger { get; }
        private protected bool Connected { get; set; }
        private protected bool IsServer { get; }
        private bool _isDatagram;

        public void Close(Exception? exception)
        {
            using IDisposable? scope = Logger.StartConnectionScope(this, IsServer);
            if (Connected || exception == null)
            {
                if (_isDatagram && IsServer)
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
                Action<Exception?> logFailure = (IsServer, _isDatagram) switch
                {
                    (false, false) => Logger.LogConnectionConnectFailed,
                    (false, true) => Logger.LogStartSendingDatagramsFailed,
                    (true, false) => Logger.LogConnectionAcceptFailed,
                    (true, true) => Logger.LogStartReceivingDatagramsFailed,
                };
                logFailure(exception);
            }
            Decoratee.Close(exception);
        }

        public virtual async ValueTask<ISingleStreamConnection> ConnectSingleStreamConnectionAsync(CancellationToken cancel)
        {
            ISingleStreamConnection singleStreamConnection = new LogSingleStreamConnectionDecorator(
                this,
                await Decoratee.ConnectSingleStreamConnectionAsync(cancel).ConfigureAwait(false));
            _isDatagram = singleStreamConnection.IsDatagram;
            LogConnected();
            return singleStreamConnection;
        }

        public async ValueTask<IMultiStreamConnection> ConnectMultiStreamConnectionAsync(CancellationToken cancel)
        {
            IMultiStreamConnection multiStreamConnection = new LogMultiStreamConnectionDecorator(
                this,
                await Decoratee.ConnectMultiStreamConnectionAsync(cancel).ConfigureAwait(false));
            LogConnected();
            return multiStreamConnection;
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => Decoratee.HasCompatibleParams(remoteEndpoint);

        public override string? ToString() => Decoratee.ToString();

        internal LogNetworkConnectionDecorator(INetworkConnection decoratee, bool isServer, ILogger logger)
        {
            Decoratee = decoratee;
            IsServer = isServer;
            Logger = logger;
        }

        internal void LogReceivedData(Memory<byte> buffer)
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
            using IDisposable? scope = Logger.StartConnectionScope(this, IsServer);
            Logger.LogReceivedData(buffer.Length, sb.ToString().Trim());
        }

        internal void LogSentData(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
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
            using IDisposable? scope = Logger.StartConnectionScope(this, IsServer);
            Logger.LogSentData(size, sb.ToString().Trim());
        }

        private protected virtual void LogConnected()
        {
            if (!Connected)
            {
                using IDisposable? scope = Logger.StartConnectionScope(this, IsServer);
                Action logSuccess = (IsServer, _isDatagram) switch
                {
                    (false, false) => Logger.LogConnectionEstablished,
                    (false, true) => Logger.LogStartSendingDatagrams,
                    (true, false) => Logger.LogConnectionAccepted,
                    (true, true) => Logger.LogStartReceivingDatagrams
                };
                logSuccess();
                Connected = true;
            }
        }
    }

    internal sealed class LogSingleStreamConnectionDecorator : ISingleStreamConnection
    {
        public int DatagramMaxReceiveSize => _decoratee.DatagramMaxReceiveSize;
        public bool IsDatagram => _decoratee.IsDatagram;

        private readonly ISingleStreamConnection _decoratee;
        private readonly LogNetworkConnectionDecorator _parent;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await _decoratee.ReadAsync(buffer, cancel).ConfigureAwait(false);
            _parent.LogReceivedData(buffer[0..received]);
            return received;
        }

        public override string? ToString() => _decoratee.ToString();

        public async ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel)
        {
            await _decoratee.WriteAsync(buffers, cancel).ConfigureAwait(false);
            _parent.LogSentData(buffers);
        }

        internal LogSingleStreamConnectionDecorator(
            LogNetworkConnectionDecorator parent,
            ISingleStreamConnection decoratee)
        {
            _parent = parent;
            _decoratee = decoratee;
        }
    }

    internal sealed class LogMultiStreamConnectionDecorator : IMultiStreamConnection
    {
        private readonly IMultiStreamConnection _decoratee;
        private readonly LogNetworkConnectionDecorator _parent;

        public async ValueTask<INetworkStream> AcceptStreamAsync(CancellationToken cancel) =>
            new LogNetworkStreamDecorator(_parent, await _decoratee.AcceptStreamAsync(cancel).ConfigureAwait(false));

        public INetworkStream CreateStream(bool bidirectional) =>
            new LogNetworkStreamDecorator(_parent, _decoratee.CreateStream(bidirectional));

        public override string? ToString() => _decoratee.ToString();

        internal LogMultiStreamConnectionDecorator(
            LogNetworkConnectionDecorator parent,
            IMultiStreamConnection decoratee)
        {
            _parent = parent;
            _decoratee = decoratee;
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
        private readonly LogNetworkConnectionDecorator _parent;

        public ReadOnlyMemory<byte> TransportHeader => _decoratee.TransportHeader;

        public void AbortRead(StreamError errorCode) => _decoratee.AbortRead(errorCode);

        public void AbortWrite(StreamError errorCode) => _decoratee.AbortWrite(errorCode);

        public Stream AsByteStream() => _decoratee.AsByteStream();

        public void EnableReceiveFlowControl() => _decoratee.EnableReceiveFlowControl();

        public void EnableSendFlowControl() => _decoratee.EnableSendFlowControl();

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await _decoratee.ReadAsync(buffer, cancel).ConfigureAwait(false);
            _parent.LogReceivedData(buffer[0..received]);
            return received;
        }

        public async ValueTask WriteAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            await _decoratee.WriteAsync(buffers, endStream, cancel).ConfigureAwait(false);
            _parent.LogSentData(buffers);
        }

        public ValueTask ShutdownCompleted(CancellationToken cancel) => _decoratee.ShutdownCompleted(cancel);

        public override string? ToString() => _decoratee.ToString();

        internal LogNetworkStreamDecorator(LogNetworkConnectionDecorator parent, INetworkStream decoratee)
        {
            _parent = parent;
            _decoratee = decoratee;
        }
    }

    internal class LogNetworkSocketConnectionDecorator : LogNetworkConnectionDecorator
    {
        private readonly NetworkSocketConnection _decoratee;

        public override string? ToString() => _decoratee.ToString();

        internal LogNetworkSocketConnectionDecorator(NetworkSocketConnection decoratee, bool isServer, ILogger logger) :
            base(decoratee, isServer, logger) => _decoratee = decoratee;

        public override async ValueTask<ISingleStreamConnection> ConnectSingleStreamConnectionAsync(
            CancellationToken cancel)
        {
            try
            {
                ISingleStreamConnection singleStreamConnection = new LogSingleStreamConnectionDecorator(
                    this,
                    await _decoratee.ConnectSingleStreamConnectionAsync(cancel).ConfigureAwait(false));

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

        private protected override void LogConnected()
        {
            if (!Connected)
            {
                using IDisposable? scope = Logger.StartConnectionScope(this, IsServer);
                Action<int, int> logSuccess = (IsServer, _decoratee.IsDatagram) switch
                {
                    (false, false) => Logger.LogSocketConnectionEstablished,
                    (false, true) => Logger.LogSocketStartSendingDatagrams,
                    (true, false) => Logger.LogSocketConnectionAccepted,
                    (true, true) => Logger.LogSocketStartReceivingDatagrams
                };
                logSuccess(_decoratee.NetworkSocket.Socket.ReceiveBufferSize,
                           _decoratee.NetworkSocket.Socket.SendBufferSize);
                Connected = true;
            }
        }
    }
}
