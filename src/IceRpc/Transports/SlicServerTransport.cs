// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IServerTransport{T}"/>.</summary>
    public class SlicServerTransport : IServerTransport<IMultiplexedNetworkConnection>
    {
        private readonly IServerTransport<ISimpleNetworkConnection> _simpleServerTransport;

        private readonly Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private readonly SlicOptions _slicOptions;

        /// <summary>Constructs a Slic server transport.</summary>
        public SlicServerTransport(IServerTransport<ISimpleNetworkConnection> simpleServerTransport)
            : this(simpleServerTransport, new SlicOptions())
        {
        }

        /// <summary>Constructs a Slic server transport.</summary>
        public SlicServerTransport(
            IServerTransport<ISimpleNetworkConnection> simpleServerTransport,
            SlicOptions slicOptions)
        {
            _simpleServerTransport = simpleServerTransport;
            _slicFrameReaderWriterFactory =
                simpleStream => (new StreamSlicFrameReader(simpleStream), new StreamSlicFrameWriter(simpleStream));
            _slicOptions = slicOptions;
        }

        IListener<IMultiplexedNetworkConnection> IServerTransport<IMultiplexedNetworkConnection>.Listen(
            Endpoint endpoint) =>
            new SlicListener(_simpleServerTransport.Listen(endpoint),
                             _slicFrameReaderWriterFactory,
                             _slicOptions);
    }
}
