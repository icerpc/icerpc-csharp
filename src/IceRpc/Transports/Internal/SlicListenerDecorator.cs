// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    internal sealed class SlicListenerDecorator : IListener
    {
        private readonly IListener _decoratee;
        private readonly Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private readonly SlicOptions _slicOptions;

        Endpoint IListener.Endpoint => _decoratee.Endpoint;

        internal SlicListenerDecorator(
            IListener decoratee,
            SlicOptions slicOptions,
            Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> slicFrameReaderWriterFactory)
        {
            _decoratee = decoratee;
            _slicOptions = slicOptions;
            _slicFrameReaderWriterFactory = slicFrameReaderWriterFactory;
        }

        async ValueTask<INetworkConnection> IListener.AcceptAsync() =>
            new SlicNetworkConnectionDecorator(
                await _decoratee.AcceptAsync().ConfigureAwait(false),
                _slicOptions,
                _slicFrameReaderWriterFactory,
                isServer: true);

        public void Dispose() => _decoratee.Dispose();
    }
}
