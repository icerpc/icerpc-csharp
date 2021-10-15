// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    // The _slicStreamFactory field is disposed by CloseAsync.
#pragma warning disable CA1001
    internal sealed class SlicNetworkConnectionDecorator : INetworkConnection
#pragma warning restore CA1001
    {
        private readonly INetworkConnection _decoratee;
        private readonly bool _isServer;
        private readonly Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private readonly SlicOptions _slicOptions;
        private SlicMultiplexedNetworkStreamFactory? _slicStreamFactory = null;

        public bool IsSecure => _decoratee.IsSecure;

        public TimeSpan LastActivity => _decoratee.LastActivity;

        public void Close(Exception? exception = null)
        {
            _slicStreamFactory?.Dispose();
            _decoratee.Close(exception);
        }

        public async Task<(IMultiplexedNetworkStreamFactory, NetworkConnectionInformation)> ConnectAndGetMultiplexedNetworkStreamFactoryAsync(
            CancellationToken cancel)
        {
            try
            {
                return await _decoratee.ConnectAndGetMultiplexedNetworkStreamFactoryAsync(cancel).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
                (INetworkStream stream, NetworkConnectionInformation information) =
                    await _decoratee.ConnectAndGetNetworkStreamAsync(cancel).ConfigureAwait(false);

                ISlicFrameReader? reader = null;
                ISlicFrameWriter? writer = null;
                try
                {
                    (reader, writer) = _slicFrameReaderWriterFactory(stream);
                    _slicStreamFactory = new SlicMultiplexedNetworkStreamFactory(
                        reader,
                        writer,
                        _isServer,
                        information.IdleTimeout,
                        _slicOptions);
                    reader = null;
                    writer = null;
                    await _slicStreamFactory.InitializeAsync(cancel).ConfigureAwait(false);

                    // Update the timeout with the timeout negotiated with the peer
                    return (_slicStreamFactory,
                            information with { IdleTimeout = _slicStreamFactory.IdleTimeout });
                }
                finally
                {
                    writer?.Dispose();
                    reader?.Dispose();
                }
            }
        }

        public Task<(INetworkStream, NetworkConnectionInformation)> ConnectAndGetNetworkStreamAsync(CancellationToken cancel) =>
            _decoratee.ConnectAndGetNetworkStreamAsync(cancel);

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => _decoratee.HasCompatibleParams(remoteEndpoint);

        internal SlicNetworkConnectionDecorator(
            INetworkConnection decoratee,
            SlicOptions slicOptions,
            Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> slicFrameReaderWriterFactory,
            bool isServer)
        {
            _decoratee = decoratee;
            _isServer = isServer;
            _slicOptions = slicOptions;
            _slicFrameReaderWriterFactory = slicFrameReaderWriterFactory;
        }
    }
}
