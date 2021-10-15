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
            _clientConnection = Connect();
            _serverConnection = await acceptTask;

            Task<(IMultiplexedNetworkStreamFactory, NetworkConnectionInformation)> multiStreamTask =
                 _serverConnection.ConnectAndGetMultiplexedNetworkStreamFactoryAsync(default);
            (_clientMultiplexedStreamFactory, _) =
                await _clientConnection.ConnectAndGetMultiplexedNetworkStreamFactoryAsync(default);
            (_serverMultiplexedStreamFactory, _) = await multiStreamTask;
        }

        protected void TearDownConnections()
        {
            _clientConnection?.Close(new ConnectionClosedException());
            _serverConnection?.Close(new ConnectionClosedException());
        }

        private async Task<INetworkConnection> AcceptAsync()
        {
            IServerTransport serverTransport = InternalTestHelper.CreateSlicDecorator(
                TestHelper.CreateServerTransport(_serverEndpoint),
                (SlicOptions?)_serverOptions ?? new());
            using IListener listener = serverTransport.Listen(_serverEndpoint);
            INetworkConnection networkConnection = await listener.AcceptAsync();
            return networkConnection;
        }

        private INetworkConnection Connect()
        {
            IClientTransport clientTransport = InternalTestHelper.CreateSlicDecorator(
                TestHelper.CreateClientTransport(_clientEndpoint),
                (SlicOptions?)_clientOptions ?? new());
            return clientTransport.CreateConnection(_clientEndpoint);
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
