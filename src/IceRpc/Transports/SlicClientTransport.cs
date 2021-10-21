// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{T}"/> using Slic.</summary>
    public class SlicClientTransport : IClientTransport<IMultiplexedNetworkConnection>
    {
        private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;
        private readonly Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> _slicFrameReaderWriterFactory;
        private readonly SlicOptions _slicOptions;

        /// <summary>Constructs a Slic client transport.</summary>
        public SlicClientTransport(IClientTransport<ISimpleNetworkConnection> simpleClientTransport)
            : this(simpleClientTransport, new())
        {
        }

        /// <summary>Constructs a Slic client transport.</summary>
        public SlicClientTransport(
            IClientTransport<ISimpleNetworkConnection> simpleClientTransport,
            SlicOptions slicOptions)
        {
            _simpleClientTransport = simpleClientTransport;
            _slicFrameReaderWriterFactory =
                simpleStream => (new StreamSlicFrameReader(simpleStream), new StreamSlicFrameWriter(simpleStream));
            _slicOptions = slicOptions;
        }

        IMultiplexedNetworkConnection IClientTransport<IMultiplexedNetworkConnection>.CreateConnection(
            Endpoint remoteEndpoint) =>
            new SlicNetworkConnection(
                _simpleClientTransport.CreateConnection(remoteEndpoint),
                isServer: false,
                _slicFrameReaderWriterFactory,
                _slicOptions);
    }
}
