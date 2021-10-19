// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    internal class SlicListenerDecorator : IListener
    {
        private readonly IListener _decoratee;
        private readonly TimeSpan _idleTimeout;
        private readonly Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private readonly SlicOptions _slicOptions;

        public Endpoint Endpoint => _decoratee.Endpoint;

        public async Task<INetworkConnection> AcceptAsync() =>
            new SlicNetworkConnectionDecorator(
                await _decoratee.AcceptAsync().ConfigureAwait(false),
                _idleTimeout,
                isServer: true,
                _slicFrameReaderWriterFactory,
                _slicOptions);

        public void Dispose() => _decoratee.Dispose();

        internal SlicListenerDecorator(
            IListener decoratee,
            TimeSpan idleTimeout,
            Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> slicFrameReaderWriterFactory,
            SlicOptions slicOptions)
        {
            _decoratee = decoratee;
            _idleTimeout = idleTimeout;
            _slicFrameReaderWriterFactory = slicFrameReaderWriterFactory;
            _slicOptions = slicOptions;
        }
    }
}
