// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace IceRpc.Transports.Internal
{
    internal sealed class TcpListener : NetworkListener
    {
        private ILogger _logger;
        private Socket _socket;

        public override async ValueTask<NetworkSocket> AcceptSocketAsync()
        {
            try
            {
                Socket fd = await _socket.AcceptAsync().ConfigureAwait(false);
                return new TcpSocket(fd, _logger);
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }
        }

        protected override void Dispose(bool disposing) => _socket.Dispose();

        internal TcpListener(Socket socket, Endpoint endpoint, ILogger logger, ServerConnectionOptions options)
            : base(endpoint, options)
        {
            _logger = logger;
            _socket = socket;
        }
    }
}
