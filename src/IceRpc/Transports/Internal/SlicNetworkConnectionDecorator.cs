// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    // Disable warning: Type 'SlicNetworkConnectionDecorator' owns disposable field(s) but is not disposable
    // The _slicMultiplexedStreamFactory is disposed by CloseAsync
#pragma warning disable CA1001
    internal class SlicNetworkConnectionDecorator : INetworkConnection
#pragma warning restore CA1001
    {
        private readonly TimeSpan _idleTimeout;
        private readonly bool _isServer;
        private readonly Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private SlicMultiplexedStreamFactory? _slicMultiplexedStreamFactory;
        private readonly SlicOptions _slicOptions;

        internal INetworkConnection Decoratee { get; }

        public bool IsSecure => Decoratee.IsSecure;

        public TimeSpan LastActivity => Decoratee.LastActivity;

        public void Close(Exception? exception = null)
        {
            Decoratee.Close(exception);
            _slicMultiplexedStreamFactory?.Dispose();
        }

        public async Task<(IMultiplexedStreamFactory, NetworkConnectionInformation)> ConnectMultiplexedAsync(
            CancellationToken cancel)
        {
            (ISimpleStream? simpleStream, NetworkConnectionInformation information) =
                await Decoratee.ConnectSimpleAsync(cancel).ConfigureAwait(false);
            if (simpleStream == null)
            {
                throw new InvalidOperationException(
                     $"expected {nameof(INetworkConnection.ConnectSimpleAsync)} to return an {nameof(ISimpleStream)}");
            }
            (ISlicFrameReader reader, ISlicFrameWriter writer) = _slicFrameReaderWriterFactory(simpleStream);
            _slicMultiplexedStreamFactory = new SlicMultiplexedStreamFactory(
                reader,
                writer,
                isServer: _isServer,
                _idleTimeout,
                _slicOptions);
            await _slicMultiplexedStreamFactory.InitializeAsync(cancel).ConfigureAwait(false);
            return (_slicMultiplexedStreamFactory,
                    information with { IdleTimeout = _slicMultiplexedStreamFactory.IdleTimeout });
        }

        public Task<(ISimpleStream, NetworkConnectionInformation)> ConnectSimpleAsync(
            CancellationToken cancel) =>
            Decoratee.ConnectSimpleAsync(cancel);

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => Decoratee.HasCompatibleParams(remoteEndpoint);

        internal SlicNetworkConnectionDecorator(
            INetworkConnection decoratee,
            TimeSpan idleTimeout,
            bool isServer,
            Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> slicFrameReaderWriterFactory,
            SlicOptions slicOptions)
        {
            Decoratee = decoratee;
            _idleTimeout = idleTimeout;
            _isServer = isServer;
            _slicFrameReaderWriterFactory = slicFrameReaderWriterFactory;
            _slicOptions = slicOptions;
        }
    }
}
