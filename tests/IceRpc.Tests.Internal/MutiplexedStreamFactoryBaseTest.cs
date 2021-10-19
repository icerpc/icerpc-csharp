// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Tests.Internal
{
    public class MultiplexedStreamFactoryBaseTest
    {
        protected INetworkConnection ClientConnection => _clientConnection!;
        protected IMultiplexedStreamFactory ClientMultiplexedStreamFactory => _clientMultiplexedStreamFactory!;
        protected INetworkConnection ServerConnection => _serverConnection!;
        protected IMultiplexedStreamFactory ServerMultiplexedStreamFactory => _serverMultiplexedStreamFactory!;

        private INetworkConnection? _clientConnection;
        private readonly Endpoint _clientEndpoint;
        private IMultiplexedStreamFactory? _clientMultiplexedStreamFactory;
        private readonly object? _clientOptions;
        private INetworkConnection? _serverConnection;
        private readonly Endpoint _serverEndpoint;
        private IMultiplexedStreamFactory? _serverMultiplexedStreamFactory;
        private readonly object? _serverOptions;

        public MultiplexedStreamFactoryBaseTest(
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

            Task<(IMultiplexedStreamFactory, NetworkConnectionInformation)> multiStreamTask =
                 _clientConnection.ConnectMultiplexedAsync(default);
            (_serverMultiplexedStreamFactory, _) = await _serverConnection.ConnectMultiplexedAsync(default);
            (_clientMultiplexedStreamFactory, _) = await multiStreamTask;
        }

        protected void TearDownConnections()
        {
            _clientConnection?.Close(new ConnectionClosedException());
            _serverConnection?.Close(new ConnectionClosedException());
        }

        private async Task<INetworkConnection> AcceptAsync()
        {
            using IListener listener =
                TestHelper.CreateServerTransport(
                    _serverEndpoint.Transport,
                    options: null,
                    multiStreamOptions: _serverOptions,
                    loggerFactory: LogAttributeLoggerFactory.Instance).Listen(_serverEndpoint);
            return await listener.AcceptAsync();
        }

        private INetworkConnection Connect()
        {
            IClientTransport clientTransport =
                TestHelper.CreateClientTransport(
                    _clientEndpoint.Transport,
                    multiStreamOptions: _clientOptions,
                    loggerFactory: LogAttributeLoggerFactory.Instance);
            return clientTransport.CreateConnection(_clientEndpoint);
        }

        protected static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateSendPayload(
            IMultiplexedStream stream,
            int length = 10)
        {
            byte[] buffer = new byte[stream.TransportHeader.Length + length];
            stream.TransportHeader.CopyTo(buffer);
            return new ReadOnlyMemory<byte>[] { buffer };
        }

        protected static Memory<byte> CreateReceivePayload(int length = 10) => new byte[length];
    }
}
