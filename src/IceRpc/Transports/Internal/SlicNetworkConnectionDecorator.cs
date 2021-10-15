// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{

    // The _slicStreamFactory field is disposed by CloseAsync.
#pragma warning disable CA1001
    internal sealed class SlicNetworkConnectionDecorator : INetworkConnection
#pragma warning restore CA1001
    {
        private readonly INetworkConnection _decoratee;
        private TimeSpan _idleTimeout;
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

        public async Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel)
        {
            NetworkConnectionInformation information = await _decoratee.ConnectAsync(cancel).ConfigureAwait(false);
            _idleTimeout = information.IdleTimeout;
            return information;
        }

        public async Task<IMultiplexedNetworkStreamFactory> GetMultiplexedNetworkStreamFactoryAsync(
            CancellationToken cancel)
        {
            try
            {
                return await _decoratee.GetMultiplexedNetworkStreamFactoryAsync(cancel).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
                INetworkStream stream = _decoratee.GetNetworkStream();

                ISlicFrameReader? reader = null;
                ISlicFrameWriter? writer = null;
                try
                {
                    (reader, writer) = _slicFrameReaderWriterFactory(stream);
                    _slicStreamFactory = new SlicMultiplexedNetworkStreamFactory(
                        reader,
                        writer,
                        _isServer,
                        _idleTimeout,
                        _slicOptions);
                    reader = null;
                    writer = null;
                    await _slicStreamFactory.InitializeAsync(cancel).ConfigureAwait(false);
                    SlicMultiplexedNetworkStreamFactory returnedSlicConnection = _slicStreamFactory;
                    _slicStreamFactory = null;
                    return returnedSlicConnection;
                }
                finally
                {
                    writer?.Dispose();
                    reader?.Dispose();
                    _slicStreamFactory?.Dispose();
                }
            }
        }

        public INetworkStream GetNetworkStream() => _decoratee.GetNetworkStream();

        public bool HasCompatibleParams(Endpoint remoteEndpoint) => throw new NotImplementedException();

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
