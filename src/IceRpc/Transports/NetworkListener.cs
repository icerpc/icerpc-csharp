// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Threading.Tasks;

namespace IceRpc.Transports
{
    /// <summary>Provides an listener that works with any <see cref="NetworkSocket"/> transport.
    /// </summary>
    public class NetworkListener : IListener
    {
        public Endpoint Endpoint { get; }

        private readonly NetworkSocket _listeningConnection;
        private readonly ServerConnectionOptions _options;

        public async ValueTask<MultiStreamConnection> AcceptAsync()
        {
            NetworkSocket serverConnection =
                await _listeningConnection.AcceptAsync().ConfigureAwait(false);

            return Endpoint.Protocol == Protocol.Ice1 ?
                new Ice1Connection(Endpoint, serverConnection, _options) :
                new SlicConnection(Endpoint, serverConnection, _options);
        }

        public void Dispose() => _listeningConnection.Dispose();

        public override string ToString() => Endpoint.ToString();

        /// <summary>Constructs an listener for a single-stream connection transport.</summary>
        /// <param name="listeningConnection">A socket that accepts connections from clients.</param>
        /// <param name="endpoint">The endpoint this listener is listening on.</param>
        /// <param name="options">The options to use when creating (accepting) connections from clients.</param>
        internal NetworkListener(
            NetworkSocket listeningConnection,
            Endpoint endpoint,
            ServerConnectionOptions options)
        {
            _listeningConnection = listeningConnection;
            Endpoint = endpoint;
            _options = options;
        }
    }
}
