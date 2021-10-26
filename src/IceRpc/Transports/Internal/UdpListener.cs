// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the UDP transport.</summary>
    internal sealed class UdpListener : IListener<ISimpleNetworkConnection>
    {
        public Endpoint Endpoint { get; }

        private readonly TaskCompletionSource<ISimpleNetworkConnection> _acceptTask = new();
        private ISimpleNetworkConnection? _serverConnection;

        public async ValueTask<ISimpleNetworkConnection> AcceptAsync()
        {
            try
            {
                if (Interlocked.Exchange(ref _serverConnection, null) is ISimpleNetworkConnection serverConnection)
                {
                    // Return the server network connection for first call
                    return serverConnection;
                }
                else
                {
                    // Wait indefinitely until Close is called
                    return await _acceptTask.Task.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                throw ExceptionUtil.Throw(ex.ToTransportException(default));
            }
        }

        public override string ToString() => Endpoint.ToString();

        public void Dispose()
        {
           // Close the server connection if AcceptAsync didn't already consume it.
           Interlocked.Exchange(ref _serverConnection, null)?.Close();
           _acceptTask.SetException(new ObjectDisposedException(nameof(UdpListener)));
        }

        internal UdpListener(Endpoint endpoint, UdpOptions options)
        {
            var serverConnection = new UdpServerNetworkConnection(endpoint, options);
            Endpoint = serverConnection.LocalEndpoint;
            _serverConnection = serverConnection;
        }
    }
}
