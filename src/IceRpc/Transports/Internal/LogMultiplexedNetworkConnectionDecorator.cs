// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal class LogMultiplexedNetworkConnectionDecorator : LogNetworkConnectionDecorator, IMultiplexedNetworkConnection
    {
        private protected override INetworkConnection Decoratee => _decoratee;

        private readonly IMultiplexedNetworkConnection _decoratee;

        public async Task<(IMultiplexedStreamFactory, NetworkConnectionInformation)> ConnectAsync(
            CancellationToken cancel)
        {
            IMultiplexedStreamFactory multiplexedStreamFactory;
            (multiplexedStreamFactory, Information) = await _decoratee.ConnectAsync(
                cancel).ConfigureAwait(false);
            multiplexedStreamFactory = new LogMultiplexedStreamFactoryDecorator(this, multiplexedStreamFactory);
            LogConnected();
            return (multiplexedStreamFactory, Information.Value);
        }

        internal static IMultiplexedNetworkConnection Decorate(
            IMultiplexedNetworkConnection decoratee,
            bool isServer,
            Endpoint endpoint,
            ILogger logger) =>
            new LogMultiplexedNetworkConnectionDecorator(decoratee, isServer, endpoint, logger);

        internal LogMultiplexedNetworkConnectionDecorator(
            IMultiplexedNetworkConnection decoratee,
            bool isServer,
            Endpoint endpoint,
            ILogger logger)
            : base(isServer, endpoint, logger) => _decoratee = decoratee;
    }

    internal sealed class LogMultiplexedStreamFactoryDecorator : IMultiplexedStreamFactory
    {
        private readonly IMultiplexedStreamFactory _decoratee;
        private readonly LogMultiplexedNetworkConnectionDecorator _parent;

        public async ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel) =>
            new LogMultiplexedStreamDecorator(
                _parent,
                await _decoratee.AcceptStreamAsync(cancel).ConfigureAwait(false));

        public IMultiplexedStream CreateStream(bool bidirectional) =>
            new LogMultiplexedStreamDecorator(_parent, _decoratee.CreateStream(bidirectional));

        public override string? ToString() => _decoratee.ToString();

        internal LogMultiplexedStreamFactoryDecorator(
            LogMultiplexedNetworkConnectionDecorator parent,
            IMultiplexedStreamFactory decoratee)
        {
            _decoratee = decoratee;
            _parent = parent;
        }
    }

    internal sealed class LogMultiplexedStreamDecorator : IMultiplexedStream
    {
        public long Id => Decoratee.Id;
        public bool IsBidirectional => Decoratee.IsBidirectional;
        public Action? ShutdownAction
        {
            get => Decoratee.ShutdownAction;
            set => Decoratee.ShutdownAction = value;
        }

        private readonly IMultiplexedStream Decoratee;
        private readonly LogNetworkConnectionDecorator _parent;

        public ReadOnlyMemory<byte> TransportHeader => Decoratee.TransportHeader;

        public void AbortRead(StreamError errorCode) => Decoratee.AbortRead(errorCode);

        public void AbortWrite(StreamError errorCode) => Decoratee.AbortWrite(errorCode);

        public Stream AsByteStream() => Decoratee.AsByteStream();

        public void EnableReceiveFlowControl() => Decoratee.EnableReceiveFlowControl();

        public void EnableSendFlowControl() => Decoratee.EnableSendFlowControl();

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            int received = await Decoratee.ReadAsync(buffer, cancel).ConfigureAwait(false);
            _parent.LogReceivedData(buffer[0..received]);
            return received;
        }

        public async ValueTask WriteAsync(
            ReadOnlyMemory<ReadOnlyMemory<byte>> buffers,
            bool endStream,
            CancellationToken cancel)
        {
            await Decoratee.WriteAsync(buffers, endStream, cancel).ConfigureAwait(false);
            _parent.LogSentData(buffers);
        }

        public ValueTask ShutdownCompleted(CancellationToken cancel) => Decoratee.ShutdownCompleted(cancel);

        public override string? ToString() => Decoratee.ToString();

        internal LogMultiplexedStreamDecorator(
            LogNetworkConnectionDecorator parent,
            IMultiplexedStream decoratee)
        {
            _parent = parent;
            Decoratee = decoratee;
        }
    }
}
