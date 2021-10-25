// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the TCP transport.</summary>
    internal sealed class TcpListener : IListener<ISimpleNetworkConnection>
    {
        public Endpoint Endpoint { get; }

        private readonly SslServerAuthenticationOptions? _authenticationOptions;
        private readonly TimeSpan _idleTimeout;
        private readonly Socket _socket;

        public async ValueTask<ISimpleNetworkConnection> AcceptAsync()
        {
            try
            {
                return new TcpServerNetworkConnection(
                    await _socket.AcceptAsync().ConfigureAwait(false),
                    Endpoint,
                    _idleTimeout,
                    _authenticationOptions);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }
        }

        public void Dispose() => _socket.Dispose();

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
