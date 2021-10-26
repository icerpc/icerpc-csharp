// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    internal class SlicListener : IListener<IMultiplexedNetworkConnection>
    {
        private readonly IListener<ISimpleNetworkConnection> _simpleListener;
        private readonly Func<ISlicFrameReader, ISlicFrameReader> _slicFrameReaderDecorator;
        private readonly Func<ISlicFrameWriter, ISlicFrameWriter> _slicFrameWriterDecorator;
        private readonly SlicOptions _slicOptions;

        public Endpoint Endpoint => _simpleListener.Endpoint;

        public async Task<IMultiplexedNetworkConnection> AcceptAsync() =>
            new SlicNetworkConnection(
                await _simpleListener.AcceptAsync().ConfigureAwait(false),
                isServer: true,
                _slicFrameReaderDecorator,
                _slicFrameWriterDecorator,
                _slicOptions);

        public void Dispose() => _simpleListener.Dispose();

        internal SlicListener(
            IListener<ISimpleNetworkConnection> simpleListener,
            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator,
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator,
            SlicOptions slicOptions)
        {
            _simpleListener = simpleListener;
            _slicFrameReaderDecorator = slicFrameReaderDecorator;
            _slicFrameWriterDecorator = slicFrameWriterDecorator;
            _slicOptions = slicOptions;
        }
    }
}
