// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    /// <summary>The <see cref="SimpleListener"/> implementation for the TCP transport.</summary>
    internal sealed class TcpListener : SimpleListener
    {
        public override Endpoint Endpoint { get; }

        private readonly SslServerAuthenticationOptions? _authenticationOptions;
        private readonly TimeSpan _idleTimeout;
        private readonly Socket _socket;

        public override async Task<SimpleNetworkConnection> AcceptAsync()
        {
            try
            {
                return new SocketNetworkConnection(
                    new TcpServerSocket(
                        await _socket.AcceptAsync().ConfigureAwait(false),
                        _authenticationOptions),
                    Endpoint,
                    isServer: true,
                    _idleTimeout);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _socket.Dispose();
            }
        }

        public override string ToString() => Endpoint.ToString();

        internal TcpListener(
            Socket socket,
            Endpoint endpoint,
            TimeSpan idleTimeout,
            SslServerAuthenticationOptions? authenticationOptions)
        {
            Endpoint = endpoint;
            _authenticationOptions = authenticationOptions;
            _socket = socket;
            _idleTimeout = idleTimeout;

            // We always call ParseTcpParams to make sure the params are ok, even when Protocol is ice1.
            _ = endpoint.ParseTcpParams();
        }
    }
}
