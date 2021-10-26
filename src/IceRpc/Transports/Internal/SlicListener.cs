// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    internal class SlicListener : IListener<IMultiplexedNetworkConnection>
    {
        private readonly IListener<ISimpleNetworkConnection> _simpleListener;
        private readonly Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private readonly SlicOptions _slicOptions;

        public Endpoint Endpoint => _simpleListener.Endpoint;

        public async Task<IMultiplexedNetworkConnection> AcceptAsync() =>
            new SlicNetworkConnection(
                await _simpleListener.AcceptAsync().ConfigureAwait(false),
                isServer: true,
                _slicFrameReaderWriterFactory,
                _slicOptions);

        public void Dispose() => _simpleListener.Dispose();

        internal SlicListener(
            IListener<ISimpleNetworkConnection> simpleListener,
            Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> slicFrameReaderWriterFactory,
            SlicOptions slicOptions)
        {
            _simpleListener = simpleListener;
            _slicFrameReaderWriterFactory = slicFrameReaderWriterFactory;
            _slicOptions = slicOptions;
        }
    }
}
