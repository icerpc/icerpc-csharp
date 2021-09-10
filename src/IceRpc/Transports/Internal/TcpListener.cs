// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the TCP transport.</summary>
    internal sealed class TcpListener : IListener
    {
        public Endpoint Endpoint { get; }

        private readonly SslServerAuthenticationOptions? _authenticationOptions;
        private readonly ILogger _logger;
        private readonly Socket _socket;
        private readonly SlicOptions _slicOptions;

        public async ValueTask<ITransportConnection> AcceptAsync()
        {
            TcpSocket tcpSocket;
            try
            {
                tcpSocket = new TcpServerSocket(
                    await _socket.AcceptAsync().ConfigureAwait(false),
                    _logger,
                    _authenticationOptions);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }

            if (Endpoint.Protocol == Protocol.Ice2)
            {
                return new SlicConnection(tcpSocket, Endpoint, isServer: true, _logger, _slicOptions);
            }
            else
            {
                return new NetworkSocketConnection(tcpSocket, Endpoint, isServer: true);
            }
        }

        public void Dispose() => _socket.Dispose();

        public override string ToString() => Endpoint.ToString();

        internal TcpListener(
            Socket socket,
            Endpoint endpoint,
            ILogger logger,
            SlicOptions slicOptions,
            SslServerAuthenticationOptions? authenticationOptions)
        {
            Endpoint = endpoint;
            _authenticationOptions = authenticationOptions;
            _logger = logger;
            _slicOptions = slicOptions;
            _socket = socket;

            // We always call ParseTcpParams to make sure the params are ok, even when Protocol is ice1.
            _ = endpoint.ParseTcpParams();
        }
    }
}
