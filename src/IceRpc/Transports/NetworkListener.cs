// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using IceRpc.Transports.Internal;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>Provides a listener for a transport implemented using a <see cref="NetworkSocket"/>.</summary>
    public abstract class NetworkListener : IListener
    {
        /// <inheritdoc/>
        public Endpoint Endpoint { get; }

        private readonly ServerConnectionOptions _options;

        /// <inheritdoc/>
        public async ValueTask<MultiStreamConnection> AcceptAsync()
        {
            NetworkSocket socket = await AcceptSocketAsync().ConfigureAwait(false);

            return Endpoint.Protocol == Protocol.Ice1 ?
                new Ice1Connection(Endpoint, socket, _options) :
                new SlicConnection(Endpoint, socket, _options);
        }

        /// <summary>Accepts a new connection and returns a <see cref="NetworkSocket"/> that represents the local
        /// (server) end of this connection.</summary>
        public abstract ValueTask<NetworkSocket> AcceptSocketAsync();

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public override string ToString() => Endpoint.ToString();

        /// <summary>Constructs a network socket listener.</summary>
        /// <param name="endpoint">The endpoint this listener is listening on.</param>
        /// <param name="options">The options used by <see cref="IListener.AcceptAsync"/> when creating a
        /// <see cref="MultiStreamConnection"/>.</param>
        protected NetworkListener(Endpoint endpoint, ServerConnectionOptions options)
        {
            Endpoint = endpoint;
            _options = options;
        }

        /// <summary>Releases the resources used by this listener.</summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only
        /// unmanaged resources.</param>
        protected abstract void Dispose(bool disposing);
    }
}
