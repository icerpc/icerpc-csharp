// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Tests.Internal
{
    public class MultiStreamConnectionBaseTest
    {
        protected INetworkConnection ClientConnection => _clientConnection!;
        protected IMultiStreamConnection ClientMultiStreamConnection => _clientMultiStreamConnection!;
        protected INetworkConnection ServerConnection => _serverConnection!;
        protected IMultiStreamConnection ServerMultiStreamConnection => _serverMultiStreamConnection!;

        private INetworkConnection? _clientConnection;
        private readonly Endpoint _clientEndpoint;
        private IMultiStreamConnection? _clientMultiStreamConnection;
        private readonly object? _clientOptions;
        private INetworkConnection? _serverConnection;
        private readonly Endpoint _serverEndpoint;
        private IMultiStreamConnection? _serverMultiStreamConnection;
        private readonly object? _serverOptions;

        public MultiStreamConnectionBaseTest(
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

            ValueTask<(IMultiStreamConnection, NetworkConnectionInformation)> multiStreamTask =
                 _serverConnection.ConnectMultiStreamConnectionAsync(default);
            (_clientMultiStreamConnection, _) = await _clientConnection.ConnectMultiStreamConnectionAsync(default);
            (_serverMultiStreamConnection, _) = await multiStreamTask;
        }

        protected void TearDownConnections()
        {
            _clientConnection?.Close(new ConnectionClosedException());
            _serverConnection?.Close(new ConnectionClosedException());
        }

        private async Task<INetworkConnection> AcceptAsync()
        {
            using IListener listener = TestHelper.CreateServerTransport(
                _serverEndpoint,
                options: null,
                multiStreamOptions: _serverOptions).Listen(_serverEndpoint).Listener!;

            return await listener.AcceptAsync();
        }

        private INetworkConnection Connect()
        {
            IClientTransport clientTransport = TestHelper.CreateClientTransport(
                _clientEndpoint,
                multiStreamOptions: _clientOptions);
            return clientTransport.CreateConnection(_clientEndpoint);
        }

        protected static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateSendPayload(INetworkStream stream, int length = 10)
        {
            byte[] buffer = new byte[stream.TransportHeader.Length + length];
            stream.TransportHeader.CopyTo(buffer);
            return new ReadOnlyMemory<byte>[] { buffer };
        }

        protected static Memory<byte> CreateReceivePayload(int length = 10) => new byte[length];
    }
}
