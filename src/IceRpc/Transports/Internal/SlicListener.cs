// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    internal class SlicListener : IListener<IMultiplexedNetworkConnection>
    {
        private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
        private readonly IListener<ISimpleNetworkConnection> _simpleListener;
        private readonly Func<ISlicFrameReader, ISlicFrameReader> _slicFrameReaderDecorator;
        private readonly Func<ISlicFrameWriter, ISlicFrameWriter> _slicFrameWriterDecorator;
        private readonly SlicTransportOptions _slicOptions;

        public Endpoint Endpoint => _simpleListener.Endpoint;

        public async Task<IMultiplexedNetworkConnection> AcceptAsync() =>
            new SlicNetworkConnection(
                await _simpleListener.AcceptAsync().ConfigureAwait(false),
                isServer: true,
                _errorCodeConverter,
                _slicFrameReaderDecorator,
                _slicFrameWriterDecorator,
                _slicOptions);

        public ValueTask DisposeAsync() => _simpleListener.DisposeAsync();

        internal SlicListener(
            IListener<ISimpleNetworkConnection> simpleListener,
            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator,
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator,
            SlicTransportOptions slicOptions)
        {
            _errorCodeConverter = simpleListener.Endpoint.Protocol.MultiplexedStreamErrorCodeConverter ??
                throw new NotSupportedException(
                    $"cannot create Slic listener for protocol {simpleListener.Endpoint.Protocol}");

            _simpleListener = simpleListener;
            _slicFrameReaderDecorator = slicFrameReaderDecorator;
            _slicFrameWriterDecorator = slicFrameWriterDecorator;
            _slicOptions = slicOptions;
        }
    }
}
