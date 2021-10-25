// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the UDP transport.</summary>
    internal sealed class UdpListener : SimpleListener
    {
        public override Endpoint Endpoint { get; }

        private readonly TaskCompletionSource<SimpleNetworkConnection> _acceptTask = new();
        private UdpSocket? _socket;

        public override async Task<SimpleNetworkConnection> AcceptAsync()
        {
            try
            {
                UdpSocket? socket = Interlocked.Exchange(ref _socket, null);
                if (socket == null)
                {
                    // Wait indefinitely until disposed if the socket was already return.
                    return await _acceptTask.Task.ConfigureAwait(false);
                }
                else
                {
                    // Return the server-side network connection.
                    return new SocketNetworkConnection(
                        socket,
                        Endpoint,
                        isServer: true,
                        idleTimeout: TimeSpan.MaxValue);
                }
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }
        }

        public override string ToString() => Endpoint.ToString();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose the UdpSocket if AcceptAsync didn't already consume it.
                Interlocked.Exchange(ref _socket, null)?.Dispose();
                _acceptTask.SetException(new ObjectDisposedException(nameof(UdpListener)));
            }
        }

        internal UdpListener(UdpSocket socket, Endpoint endpoint)
        {
            Endpoint = endpoint;
            _socket = socket;
        }
    }
}
