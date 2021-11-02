// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    internal class LogSimpleNetworkConnectionDecorator : LogNetworkConnectionDecorator, ISimpleNetworkConnection
    {
        private protected override INetworkConnection Decoratee => _decoratee;

        private readonly ISimpleNetworkConnection _decoratee;

        public virtual async Task<(ISimpleStream, NetworkConnectionInformation)> ConnectAsync(CancellationToken cancel)
        {
            ISimpleStream simpleStream;
            try
            {
                (simpleStream, Information) = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogConnectFailed(ex);
                throw;
            }

            simpleStream = new LogSimpleStreamDecorator(this, simpleStream);
            IsDatagram = simpleStream.IsDatagram;

            LogConnected();
            return (simpleStream, Information.Value);
        }

        internal static ISimpleNetworkConnection Decorate(
            ISimpleNetworkConnection decoratee,
            bool isServer,
            Endpoint endpoint,
            ILogger logger) =>
            new LogSimpleNetworkConnectionDecorator(decoratee, isServer, endpoint, logger);

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
}
