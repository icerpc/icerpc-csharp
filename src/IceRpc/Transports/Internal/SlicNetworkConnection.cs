// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    // Disable warning: Type 'SlicNetworkConnectionDecorator' owns disposable field(s) but is not disposable
    // The _slicMultiplexedStreamFactory is disposed by CloseAsync
#pragma warning disable CA1001
    internal class SlicNetworkConnection : IMultiplexedNetworkConnection
#pragma warning restore CA1001
    {
        public bool IsSecure => _simpleNetworkConnection.IsSecure;

        public TimeSpan LastActivity => _simpleNetworkConnection.LastActivity;

        private readonly bool _isServer;

        private readonly ISimpleNetworkConnection _simpleNetworkConnection;
        private readonly Func<ISlicFrameReader, ISlicFrameReader> _slicFrameReaderDecorator;
        private readonly Func<ISlicFrameWriter, ISlicFrameWriter> _slicFrameWriterDecorator;

        private SlicMultiplexedStreamFactory? _slicMultiplexedStreamFactory;
        private readonly SlicOptions _slicOptions;

        public void Close(Exception? exception = null)
        {
            _simpleNetworkConnection.Close(exception);
            _slicMultiplexedStreamFactory?.Dispose();
        }

        async Task<(IMultiplexedStreamFactory, NetworkConnectionInformation)> IMultiplexedNetworkConnection.ConnectAsync(
            CancellationToken cancel)
        {
            (ISimpleStream simpleStream, NetworkConnectionInformation information) =
                await _simpleNetworkConnection.ConnectAsync(cancel).ConfigureAwait(false);

            _slicMultiplexedStreamFactory = new SlicMultiplexedStreamFactory(
                simpleStream,
                _slicFrameReaderDecorator,
                _slicFrameWriterDecorator,
                isServer: _isServer,
                information.IdleTimeout,
                _slicOptions);
            await _slicMultiplexedStreamFactory.InitializeAsync(cancel).ConfigureAwait(false);
            return (_slicMultiplexedStreamFactory,
                    information with { IdleTimeout = _slicMultiplexedStreamFactory.IdleTimeout });
        }

        public bool HasCompatibleParams(Endpoint remoteEndpoint) =>
            _simpleNetworkConnection.HasCompatibleParams(remoteEndpoint);

        internal SlicNetworkConnection(
            ISimpleNetworkConnection simpleNetworkConnection,
            bool isServer,
            Func<ISlicFrameReader, ISlicFrameReader> slicFrameReaderDecorator,
            Func<ISlicFrameWriter, ISlicFrameWriter> slicFrameWriterDecorator,
            SlicOptions slicOptions)
        {
            _simpleNetworkConnection = simpleNetworkConnection;
            _isServer = isServer;
            _slicFrameReaderDecorator = slicFrameReaderDecorator;
            _slicFrameWriterDecorator = slicFrameWriterDecorator;
            _slicOptions = slicOptions;
        }
    }
}
