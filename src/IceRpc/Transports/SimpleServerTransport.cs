// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>The base class for simple server transports. Multiplexed stream support is provided by Slic.</summary>
    public abstract class SimpleServerTransport : IServerTransport
    {
        private readonly TimeSpan _idleTimeout;
        private readonly SlicOptions _slicOptions;

        internal Func<ISimpleStream, (ISlicFrameReader, ISlicFrameWriter)> SlicFrameReaderWriterFactory { get; set; }

        /// <summary>Starts listening on an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The new listener.</returns>
        /// <exception name="UnknownTransportException">Thrown if this server transport does not support the endpoint's
        /// transport.</exception>
        protected abstract SimpleListener Listen(Endpoint endpoint);

        /// <summary>Constructs a <see cref="SimpleServerTransport"/>.</summary>
        /// <param name="idleTimeout">The idle timeout.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        protected SimpleServerTransport(SlicOptions slicOptions, TimeSpan idleTimeout)
        {
            SlicFrameReaderWriterFactory = simpleStream => (
                new StreamSlicFrameReader(simpleStream),
                new StreamSlicFrameWriter(simpleStream));

            _idleTimeout = idleTimeout;
            _slicOptions = slicOptions;
        }

        IListener IServerTransport.Listen(Endpoint endpoint) =>
            new SlicListenerDecorator(Listen(endpoint), _idleTimeout, SlicFrameReaderWriterFactory, _slicOptions);
    }
}
