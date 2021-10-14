// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>Implements <see cref="IClientTransport"/> to create an <see cref="IMultiplexedNetworkStreamFactory"/>
    /// for transports that only support <see cref="INetworkStream"/></summary>
    internal sealed class SlicClientTransportDecorator : IClientTransport
    {
        private readonly IClientTransport _decoratee;
        private readonly Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private readonly SlicOptions _slicOptions;

        internal SlicClientTransportDecorator(
            IClientTransport decoratee,
            SlicOptions slicOptions,
            Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> slicFrameReaderWriterFactory)
        {
            _decoratee = decoratee;
            _slicOptions = slicOptions;
            _slicFrameReaderWriterFactory = slicFrameReaderWriterFactory;
        }

        INetworkConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint) =>
            new SlicNetworkConnectionDecorator(
                _decoratee.CreateConnection(remoteEndpoint),
                _slicOptions,
                _slicFrameReaderWriterFactory,
                isServer: false);
    }
}
