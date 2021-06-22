// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
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

        internal SingleStreamConnectionAcceptor(
            Endpoint endpoint,
            IncomingConnectionOptions options,
            SingleStreamConnection listeningConnection)
        {
            _options = options;
            _listeningConnection = listeningConnection;

            Endpoint = endpoint;
        }
    }
}
