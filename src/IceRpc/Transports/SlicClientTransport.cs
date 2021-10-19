// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

namespace IceRpc.Transports
{
    /// <summary>Provides multiplexed stream support for network stream based <see cref="IServerTransport"/>.</summary>
    public abstract class SlicClientTransport : IClientTransport
    {
        private readonly TimeSpan _idleTimeout;
        private readonly SlicOptions _slicOptions;

        internal Func<INetworkStream, (ISlicFrameReader, ISlicFrameWriter)> SlicFrameReaderWriterFactory { get; set; }

        /// <summary>Creates a network connection that supports <see cref="INetworkStream"/></summary>
        /// <param name="remoteEndpoint">The remote endpoint.</param>
        /// <returns>The <see cref="INetworkConnection"/> that supports return an <see cref="INetworkStream"/> from
        /// <see cref="INetworkConnection.ConnectAsync"/>.</returns>
        protected abstract INetworkConnection CreateConnection(Endpoint remoteEndpoint);

        /// <summary>Constructs a <see cref="SlicClientTransport"/>.</summary>
        /// <param name="slicOptions">The Slic transport options.</param>
        /// <param name="idleTimeout">The idle timeout.</param>
        protected SlicClientTransport(SlicOptions slicOptions, TimeSpan idleTimeout)
        {
            SlicFrameReaderWriterFactory = networkStream => (
                new StreamSlicFrameReader(networkStream),
                new StreamSlicFrameWriter(networkStream));

            _idleTimeout = idleTimeout;
            _slicOptions = slicOptions;
        }

        INetworkConnection IClientTransport.CreateConnection(Endpoint remoteEndpoint) =>
            remoteEndpoint.Protocol.RequiresMultiplexedTransport ?
                new SlicNetworkConnectionDecorator(
                    CreateConnection(remoteEndpoint),
                    _idleTimeout,
                    isServer: false,
                    SlicFrameReaderWriterFactory,
                    _slicOptions) :
                CreateConnection(remoteEndpoint);
    }
}
