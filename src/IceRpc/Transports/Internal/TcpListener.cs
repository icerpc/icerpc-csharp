// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the TCP transport.</summary>
    internal sealed class TcpListener : IListener
    {
        public Endpoint Endpoint { get; }

        private readonly SslServerAuthenticationOptions? _authenticationOptions;
        private readonly TimeSpan _idleTimeout;
        private readonly Socket _socket;
        private readonly SlicOptions _slicOptions;

        public async ValueTask<INetworkConnection> AcceptAsync()
        {
            try
            {
                return new SocketNetworkConnection(
                    new TcpServerSocket(
                        await _socket.AcceptAsync().ConfigureAwait(false),
                        _authenticationOptions),
                    Endpoint,
                    isServer: true,
                    _idleTimeout,
                    _slicOptions);
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
            SlicOptions slicOptions,
            SslServerAuthenticationOptions? authenticationOptions)
        {
            Endpoint = endpoint;
            _authenticationOptions = authenticationOptions;
            _slicOptions = slicOptions;
            _socket = socket;
            _idleTimeout = idleTimeout;

            // We always call ParseTcpParams to make sure the params are ok, even when Protocol is ice1.
            _ = endpoint.ParseTcpParams();
        }
    }
}
