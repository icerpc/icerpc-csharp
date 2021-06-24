// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    /// <summary>Provides an acceptor that works with any <see cref="SingleStreamConnection"/> transport.
    /// </summary>
    internal class SingleStreamConnectionAcceptor : IAcceptor
    {
        public Endpoint Endpoint { get; }

        private readonly SingleStreamConnection _listeningConnection;
        private readonly IncomingConnectionOptions _options;

        public async ValueTask<MultiStreamConnection> AcceptAsync()
        {
            SingleStreamConnection serverConnection =
                await _listeningConnection.AcceptAsync().ConfigureAwait(false);

            return Endpoint.Protocol == Protocol.Ice1 ?
                new Ice1Connection(Endpoint, serverConnection, _options) :
                new SlicConnection(Endpoint, serverConnection, _options);
        }

        public void Dispose() => _listeningConnection.Dispose();

        public override string ToString() => Endpoint.ToString();

        /// <summary>Constructs an acceptor for a single-stream connection transport.</summary>
        /// <param name="listeningConnection">A socket that accepts connections from clients.</param>
        /// <param name="endpoint">The endpoint this acceptor is listening on.</param>
        /// <param name="options">The options to use when creating (accepting) connections from clients.</param>
        internal SingleStreamConnectionAcceptor(
            SingleStreamConnection listeningConnection,
            Endpoint endpoint,
            IncomingConnectionOptions options)
        {
            _listeningConnection = listeningConnection;
            Endpoint = endpoint;
            _options = options;
        }
    }
}
