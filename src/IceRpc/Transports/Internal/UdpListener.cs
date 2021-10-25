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
                if (Interlocked.Exchange(ref _socket, null) is UdpSocket socket)
                {
                    // Return the server-side network connection if the socket wasn't already consumed.
                    return new SocketNetworkConnection(
                        socket,
                        Endpoint,
                        isServer: true,
                        idleTimeout: TimeSpan.MaxValue);
                }
                else
                {
                    // Wait indefinitely until Dispose is called if the socket was already consumed.
                    return await _acceptTask.Task.ConfigureAwait(false);
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
