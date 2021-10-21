// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;

namespace IceRpc.Transports.Internal
{
    internal class LogSimpleNetworkConnectionDecorator : LogNetworkConnectionDecorator, ISimpleNetworkConnection
    {
        private protected override INetworkConnection Decoratee => _decoratee;

        private readonly ISimpleNetworkConnection _decoratee;

        public virtual async Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel)
        {
            ISimpleStream simpleStream;
            (simpleStream, Information) = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
            simpleStream = new LogSimpleStreamDecorator(this, simpleStream);
            IsDatagram = simpleStream.IsDatagram;

            LogConnected();
            return (simpleStream, Information.Value);
        }

        internal static ISimpleNetworkConnection Decorate(
            ISimpleNetworkConnection decoratee,
            bool isServer,
            Endpoint endpoint,
            ILogger logger)
        {
            if (decoratee is SocketNetworkConnection socketNetworkConnection)
            {
                return new LogSocketNetworkConnectionDecorator(socketNetworkConnection,
                                                               isServer,
                                                               endpoint,
                                                               logger);
            }
            else
            {
                return new LogSimpleNetworkConnectionDecorator(decoratee, isServer, endpoint, logger);
            }
        }

        internal LogSimpleNetworkConnectionDecorator(
            ISimpleNetworkConnection decoratee,
            bool isServer,
            Endpoint endpoint,
            ILogger logger)
            : base(isServer, endpoint, logger) => _decoratee = decoratee;
    }

    internal sealed class LogSimpleStreamDecorator : ISimpleStream
    {
        public int DatagramMaxReceiveSize => _decoratee.DatagramMaxReceiveSize;
        public bool IsDatagram => _decoratee.IsDatagram;

        private readonly ISimpleStream _decoratee;
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

        internal LogSimpleStreamDecorator(
            LogNetworkConnectionDecorator parent,
            ISimpleStream decoratee)
        {
            _parent = parent;
            _decoratee = decoratee;
        }
    }

    internal class LogSocketNetworkConnectionDecorator : LogSimpleNetworkConnectionDecorator
    {
        private readonly SocketNetworkConnection _decoratee;

        internal LogSocketNetworkConnectionDecorator(
            SocketNetworkConnection decoratee,
            bool isServer,
            Endpoint endpoint,
            ILogger logger) :
            base(decoratee, isServer, endpoint, logger) => _decoratee = decoratee;

        public override async Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel)
        {
            try
            {
                (ISimpleStream simpleStream, Information) = await base.ConnectAsync(cancel).ConfigureAwait(false);

                if (_decoratee.NetworkSocket.SslStream is SslStream sslStream)
                {
                    Logger.LogTlsAuthenticationSucceeded(sslStream);
                }
                return (simpleStream, Information.Value);
            }
            catch (TransportException exception) when (exception.InnerException is AuthenticationException ex)
            {
                Logger.LogTlsAuthenticationFailed(ex);
                throw;
            }
        }

        private protected override void LogConnected()
        {
            using IDisposable? scope = Logger.StartConnectionScope(Information!.Value, IsServer);
            Action<int, int> logSuccess = (IsServer, _decoratee.IsDatagram) switch
            {
                (false, false) => Logger.LogSocketNetworkConnectionEstablished,
                (false, true) => Logger.LogSocketStartSendingDatagrams,
                (true, false) => Logger.LogSocketNetworkConnectionAccepted,
                (true, true) => Logger.LogSocketStartReceivingDatagrams
            };
            logSuccess(_decoratee.NetworkSocket.Socket.ReceiveBufferSize,
                       _decoratee.NetworkSocket.Socket.SendBufferSize);
        }
    }
}
