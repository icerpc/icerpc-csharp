// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    /// <summary>The listener implementation for the UDP transport.</summary>
    internal sealed class UdpListener : IListener<ISimpleNetworkConnection>
    {
        public Endpoint Endpoint { get; }
        private readonly ISimpleNetworkConnection _serverConnection;

        public Task<ISimpleNetworkConnection> AcceptAsync() => Task.FromResult(_serverConnection);

        public override string ToString() => Endpoint.ToString();

        public void Dispose() => _serverConnection.Close();

        internal UdpListener(Endpoint endpoint, ISimpleNetworkConnection serverConnection)
        {
            Endpoint = endpoint;
            _serverConnection = serverConnection;
        }
    }
}
