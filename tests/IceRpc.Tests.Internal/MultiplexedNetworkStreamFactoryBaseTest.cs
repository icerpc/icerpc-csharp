// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Tests.Internal
{
    public class MultiplexedNetworkStreamFactoryBaseTest
    {
        protected INetworkConnection ClientConnection => _clientConnection!;
        protected IMultiplexedNetworkStreamFactory ClientMultiplexedStreamFactory => _clientMultiplexedStreamFactory!;
        protected INetworkConnection ServerConnection => _serverConnection!;
        protected IMultiplexedNetworkStreamFactory ServerMultiplexedStreamFactory => _serverMultiplexedStreamFactory!;

        private INetworkConnection? _clientConnection;
        private readonly Endpoint _clientEndpoint;
        private IMultiplexedNetworkStreamFactory? _clientMultiplexedStreamFactory;
        private readonly object? _clientOptions;
        private INetworkConnection? _serverConnection;
        private readonly Endpoint _serverEndpoint;
        private IMultiplexedNetworkStreamFactory? _serverMultiplexedStreamFactory;
        private readonly object? _serverOptions;

        public MultiplexedNetworkStreamFactoryBaseTest(
            string clientEndpoint = "ice+coloc://127.0.0.1",
            object? clientOptions = null,
            string serverEndpoint = "ice+coloc://127.0.0.1",
            object? serverOptions = null)
        {
            _clientEndpoint = clientEndpoint;
            _clientOptions = clientOptions;
            _serverEndpoint = serverEndpoint;
            _serverOptions = serverOptions;
        }

        protected async Task SetUpConnectionsAsync()
        {
            Task<INetworkConnection> acceptTask = AcceptAsync();
            _clientConnection = await ConnectAsync();
            _serverConnection = await acceptTask;

            Task<IMultiplexedNetworkStreamFactory> multiStreamTask =
                 _serverConnection.GetMultiplexedNetworkStreamFactoryAsync(default);
            _clientMultiplexedStreamFactory = await _clientConnection.GetMultiplexedNetworkStreamFactoryAsync(default);
            _serverMultiplexedStreamFactory = await multiStreamTask;
        }

        protected void TearDownConnections()
        {
            _clientConnection?.Close(new ConnectionClosedException());
            _serverConnection?.Close(new ConnectionClosedException());
        }

        private async Task<INetworkConnection> AcceptAsync()
        {
            using IListener listener = new SlicServerTransportDecorator(
                TestHelper.CreateServerTransport(_serverEndpoint).Listen(_serverEndpoint),
                _serverOptions);
            INetworkConnection networkConnection = await listener.AcceptAsync();
            await networkConnection.ConnectAsync(default);
            return networkConnection;
        }

        private async Task<INetworkConnection> ConnectAsync()
        {
            IClientTransport clientTransport = new SlicClientTransportDecorator(
                TestHelper.CreateClientTransport(_clientEndpoint),
                _clientOptions);
            INetworkConnection networkConnection = clientTransport.CreateConnection(_clientEndpoint);
            await networkConnection.ConnectAsync(default);
            return networkConnection;
        }

        protected static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateSendPayload(
            IMultiplexedNetworkStream stream,
            int length = 10)
        {
            byte[] buffer = new byte[stream.TransportHeader.Length + length];
            stream.TransportHeader.CopyTo(buffer);
            return new ReadOnlyMemory<byte>[] { buffer };
        }

        protected static Memory<byte> CreateReceivePayload(int length = 10) => new byte[length];
    }
}
