// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
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

        public async ValueTask<INetworkConnection> AcceptAsync()
        {
            try
            {
                return LogNetworkConnectionDecorator.Create(
                    new NetworkSocketConnection(
                        new TcpServerSocket(
                            await _socket.AcceptAsync().ConfigureAwait(false),
                            _authenticationOptions),
                        Endpoint,
                        isServer: true,
                        _slicOptions,
                        _logger),
                    _logger);
            }
            catch (Exception ex)
            {
                throw IceRpc.Internal.ExceptionUtil.Throw(ex.ToTransportException(default));
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
