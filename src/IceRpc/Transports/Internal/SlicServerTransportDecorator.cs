// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>Implements <see cref="IServerTransport"/> to create an <see cref="IMultiplexedNetworkStreamFactory"/>
    /// for transports that only support <see cref="INetworkStream"/></summary>
    internal sealed class SlicServerTransportDecorator : IServerTransport
    {
        private readonly IServerTransport _decoratee;
        private readonly Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private readonly SlicOptions _slicOptions;

        internal SlicServerTransportDecorator(
            IServerTransport decoratee,
            SlicOptions slicOptions,
            Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> slicFrameReaderWriterFactory)
        {
            _decoratee = decoratee;
            _slicOptions = slicOptions;
            _slicFrameReaderWriterFactory = slicFrameReaderWriterFactory;
        }

        IListener IServerTransport.Listen(Endpoint endpoint) =>
            new SlicListenerDecorator(
                _decoratee.Listen(endpoint),
                _slicOptions,
                _slicFrameReaderWriterFactory);
    }
}
