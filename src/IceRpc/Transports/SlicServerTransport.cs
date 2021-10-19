// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Provides multiplexed stream support for network stream based  <see cref="IServerTransport"/>.</summary>
    public abstract class SlicServerTransport : IServerTransport
    {
        private readonly TimeSpan _idleTimeout;
        private readonly SlicOptions _slicOptions;

        internal Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> SlicFrameReaderWriterFactory { get; set; }

        /// <summary>Starts listening on an endpoint.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The new listener.</returns>
        /// <exception name="UnknownTransportException">Thrown if this server transport does not support the endpoint's
        /// transport.</exception>
        protected abstract IListener Listen(Endpoint endpoint);

        /// <summary>Constructs a <see cref="SlicServerTransport"/>.</summary>
        /// <param name="idleTimeout">The idle timeout.</param>
        /// <param name="slicOptions">The Slic transport options.</param>
        protected SlicServerTransport(SlicOptions slicOptions, TimeSpan idleTimeout)
        {
            SlicFrameReaderWriterFactory = networkStream => (
                new StreamSlicFrameReader(networkStream),
                new StreamSlicFrameWriter(networkStream));

            _idleTimeout = idleTimeout;
            _slicOptions = slicOptions;
        }

        IListener IServerTransport.Listen(Endpoint endpoint) =>
            endpoint.Protocol.RequiresMultiplexedTransport ?
                new SlicListenerDecorator(Listen(endpoint), _idleTimeout, SlicFrameReaderWriterFactory, _slicOptions) :
                Listen(endpoint);
    }
}
