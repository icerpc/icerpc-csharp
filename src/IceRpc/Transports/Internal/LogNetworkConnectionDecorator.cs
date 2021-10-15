// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal class LogNetworkConnectionDecorator : INetworkConnection
    {
        public bool IsSecure => _decoratee.IsSecure;
        public TimeSpan LastActivity => _decoratee.LastActivity;

        protected NetworkConnectionInformation? Information { get; set; }

        private protected bool IsServer { get; }
        private protected ILogger Logger { get; }

        private readonly INetworkConnection _decoratee;
        private readonly Endpoint _endpoint;
        private bool _isDatagram;

        public void Close(Exception? exception)
        {
            if (Information == null)
            {
                using IDisposable? scope = Logger.StartConnectionScope(_endpoint, IsServer);

                // If the connection is connecting but not active yet, we print a trace to show that the
                // connection got connected or accepted before printing out the connection closed trace.
                Action<Exception?> logFailure = (IsServer, _isDatagram) switch
                {
                    (false, false) => Logger.LogConnectionConnectFailed,
                    (false, true) => Logger.LogStartSendingDatagramsFailed,
                    (true, false) => Logger.LogConnectionAcceptFailed,
                    (true, true) => Logger.LogStartReceivingDatagramsFailed,
                };
                logFailure(exception);
            }
            else
            {
                using IDisposable? scope = Logger.StartConnectionScope(Information.Value, IsServer);
                if (_isDatagram && IsServer)
                {
                    Logger.LogStopReceivingDatagrams();
                }
                else
                {
                    Logger.LogConnectionClosed(exception?.Message ?? "graceful close");
                }
            }
            _decoratee.Close(exception);
        }

        public async Task<(IMultiplexedNetworkStreamFactory, NetworkConnectionInformation)> ConnectAndGetMultiplexedNetworkStreamFactoryAsync(
            CancellationToken cancel)
        {
            IMultiplexedNetworkStreamFactory streamFactory;
            (streamFactory, Information) = await _decoratee.ConnectAndGetMultiplexedNetworkStreamFactoryAsync(
                cancel).ConfigureAwait(false);
            streamFactory = new LogMultiplexedStreamFactoryDecorator(this, streamFactory);
            LogConnected();
            return (streamFactory, Information.Value);
        }

        public async Task<(INetworkStream, NetworkConnectionInformation)> ConnectAndGetNetworkStreamAsync(
            CancellationToken cancel)
        {
            INetworkStream networkStream;
            (networkStream, Information) = await _decoratee.ConnectAndGetNetworkStreamAsync(
                cancel).ConfigureAwait(false);
            networkStream = new LogNetworkStreamDecorator(this, networkStream);
            _isDatagram = networkStream.IsDatagram;
            LogConnected();
            return (networkStream, Information.Value);
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => _decoratee.HasCompatibleParams(remoteEndpoint);

        public override string? ToString() => _decoratee.ToString();

        internal LogNetworkConnectionDecorator(
            INetworkConnection decoratee,
            bool isServer,
            Endpoint endpoint,
            ILogger logger)
        {
            _decoratee = decoratee;
            _endpoint = endpoint;
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
            using IDisposable? scope = Logger.StartConnectionScope(Information!.Value, IsServer);
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
            using IDisposable? scope = Logger.StartConnectionScope(Information!.Value, IsServer);
            Logger.LogSentData(size, sb.ToString().Trim());
        }

        private protected virtual void LogConnected()
        {
            using IDisposable? scope = Logger.StartConnectionScope(Information!.Value, IsServer);
            Action logSuccess = (IsServer, _isDatagram) switch
            {
                (false, false) => Logger.LogConnectionEstablished,
                (false, true) => Logger.LogStartSendingDatagrams,
                (true, false) => Logger.LogConnectionAccepted,
                (true, true) => Logger.LogStartReceivingDatagrams
            };
            logSuccess();
        }
    }

    internal sealed class LogNetworkStreamDecorator : INetworkStream
    {
        public int DatagramMaxReceiveSize => _decoratee.DatagramMaxReceiveSize;
        public bool IsDatagram => _decoratee.IsDatagram;

        private readonly INetworkStream _decoratee;
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

        internal LogNetworkStreamDecorator(LogNetworkConnectionDecorator parent, INetworkStream decoratee)
        {
            _parent = parent;
            _decoratee = decoratee;
        }
    }

    internal sealed class LogMultiplexedStreamFactoryDecorator : IMultiplexedNetworkStreamFactory
    {
        private readonly IMultiplexedNetworkStreamFactory _decoratee;
        private readonly LogNetworkConnectionDecorator _parent;

        public async ValueTask<IMultiplexedNetworkStream> AcceptStreamAsync(CancellationToken cancel) =>
            new LogMultiplexNetworkStreamDecorator(
                _parent,
                await _decoratee.AcceptStreamAsync(cancel).ConfigureAwait(false));

        public IMultiplexedNetworkStream CreateStream(bool bidirectional) =>
            new LogMultiplexNetworkStreamDecorator(_parent, _decoratee.CreateStream(bidirectional));

        public override string? ToString() => _decoratee.ToString();

        internal LogMultiplexedStreamFactoryDecorator(
            LogNetworkConnectionDecorator parent,
            IMultiplexedNetworkStreamFactory decoratee)
        {
            _parent = parent;
            _decoratee = decoratee;
        }
    }

    internal sealed class LogMultiplexNetworkStreamDecorator : IMultiplexedNetworkStream
    {
        public long Id => _decoratee.Id;
        public bool IsBidirectional => _decoratee.IsBidirectional;
        public Action? ShutdownAction
        {
            get => _decoratee.ShutdownAction;
            set => _decoratee.ShutdownAction = value;
        }

        private readonly IMultiplexedNetworkStream _decoratee;
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

        internal LogMultiplexNetworkStreamDecorator(
            LogNetworkConnectionDecorator parent,
            IMultiplexedNetworkStream decoratee)
        {
            _parent = parent;
            _decoratee = decoratee;
        }
    }

    internal class LogNetworkSocketConnectionDecorator : LogNetworkConnectionDecorator
    {
        private readonly SocketNetworkConnection _decoratee;

        public override string? ToString() => _decoratee.ToString();

        internal LogNetworkSocketConnectionDecorator(
            SocketNetworkConnection decoratee,
            bool isServer,
            Endpoint endpoint,
            ILogger logger) :
            base(decoratee, isServer, endpoint, logger) => _decoratee = decoratee;

        private protected override void LogConnected()
        {
            using IDisposable? scope = Logger.StartConnectionScope(Information!.Value, IsServer);
            Action<int, int> logSuccess = (IsServer, _decoratee.IsDatagram) switch
            {
                (false, false) => Logger.LogSocketConnectionEstablished,
                (false, true) => Logger.LogSocketStartSendingDatagrams,
                (true, false) => Logger.LogSocketConnectionAccepted,
                (true, true) => Logger.LogSocketStartReceivingDatagrams
            };
            logSuccess(_decoratee.NetworkSocket.Socket.ReceiveBufferSize,
                        _decoratee.NetworkSocket.Socket.SendBufferSize);
        }
    }
}
