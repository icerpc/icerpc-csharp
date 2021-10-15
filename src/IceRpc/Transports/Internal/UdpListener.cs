// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the UDP transport.</summary>
    internal sealed class UdpListener : IListener
    {
        public Endpoint Endpoint { get; }

        private readonly ManualResetValueTaskCompletionSource<UdpSocket> _acceptTask = new();

        public async ValueTask<INetworkConnection> AcceptAsync()
        {
            try
            {
                return new SocketNetworkConnection(
                    await _acceptTask.ValueTask.ConfigureAwait(false),
                    Endpoint,
                    isServer: true,
                    defaultIdleTimeout: TimeSpan.MaxValue,
                    new());
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }
        }

        public void Dispose()
        {
            if (_acceptTask.ValueTask.IsCompletedSuccessfully)
            {
                _acceptTask.ValueTask.Result.Dispose();
            }
            else
            {
                _acceptTask.SetException(new ObjectDisposedException(nameof(UdpListener)));
            }
        }

        public override string ToString() => Endpoint.ToString();

        internal UdpListener(UdpSocket socket, Endpoint endpoint)
        {
            Endpoint = endpoint;

            // Set the socket that will be returned the first time AcceptAsync is called. Once returned,
            // the next AcceptAsync call will block indefinitely since we won't provide a new socket.
            _acceptTask.SetResult(socket);
        }
    }
}
