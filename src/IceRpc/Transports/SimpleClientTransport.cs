// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>The base class for simple client transports. Multiplexed stream support is provided by Slic.</summary>
    public abstract class SimpleClientTransport : IClientTransport
    {
        private readonly TimeSpan _idleTimeout;
        private readonly SlicOptions _slicOptions;

        internal Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> SlicFrameReaderWriterFactory { get; set; }

        /// <summary>Creates a network connection that supports <see cref="ISimpleStream"/>. Multiplexed stream support
        /// is provided by Slic.</summary>
        /// <param name="remoteEndpoint">The remote endpoint.</param>
        /// <returns>The <see cref="SimpleNetworkConnection"/>.</returns>
        protected abstract SimpleNetworkConnection CreateConnection(Endpoint remoteEndpoint);

        /// <summary>Constructs a <see cref="SimpleClientTransport"/>.</summary>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <param name="idleTimeout">The idle timeout.</param>
        protected SimpleClientTransport(SlicOptions slicOptions, TimeSpan idleTimeout)
        {
            SlicFrameReaderWriterFactory = simpleStream => (
                new StreamSlicFrameReader(simpleStream),
                new StreamSlicFrameWriter(simpleStream));

            _idleTimeout = idleTimeout;
            _slicOptions = slicOptions;
        }

        INetworkConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint) =>
            new SlicNetworkConnectionDecorator(
                CreateConnection(remoteEndpoint),
                _idleTimeout,
                isServer: false,
                SlicFrameReaderWriterFactory,
                _slicOptions);
    }
}
