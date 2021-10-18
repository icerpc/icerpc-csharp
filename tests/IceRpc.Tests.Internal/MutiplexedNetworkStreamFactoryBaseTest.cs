// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;

namespace IceRpc.Tests.Internal
{
    public class MultiplexedNetworkStreamFactoryBaseTest
    {
        protected INetworkConnection ClientConnection => _clientConnection!;
        protected IMultiplexedNetworkStreamFactory ClientMultiplexedNetworkStreamFactory => _clientMultiplexedNetworkStreamFactory!;
        protected INetworkConnection ServerConnection => _serverConnection!;
        protected IMultiplexedNetworkStreamFactory ServerMultiplexedNetworkStreamFactory => _serverMultiplexedNetworkStreamFactory!;

        private INetworkConnection? _clientConnection;
        private readonly Endpoint _clientEndpoint;
        private IMultiplexedNetworkStreamFactory? _clientMultiplexedNetworkStreamFactory;
        private readonly object? _clientOptions;
        private INetworkConnection? _serverConnection;
        private readonly Endpoint _serverEndpoint;
        private IMultiplexedNetworkStreamFactory? _serverMultiplexedNetworkStreamFactory;
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

            Task<(INetworkStream?, IMultiplexedNetworkStreamFactory?, NetworkConnectionInformation)> multiStreamTask =
                 _clientConnection.ConnectAsync(true, default);
            (_, _serverMultiplexedNetworkStreamFactory, _) = await _serverConnection.ConnectAsync(true, default);
            (_, _clientMultiplexedNetworkStreamFactory, _) = await multiStreamTask;
        }

        protected void TearDownConnections()
        {
            _clientConnection?.Close(new ConnectionClosedException());
            _serverConnection?.Close(new ConnectionClosedException());
        }

        private async Task<INetworkConnection> AcceptAsync()
        {
            using IListener listener = new LogServerTransportDecorator(
                TestHelper.CreateServerTransport(
                    _serverEndpoint,
                    options: null,
                    multiStreamOptions: _serverOptions),
                LogAttributeLoggerFactory.Instance.CreateLogger("IceRpc.Transports")).Listen(_serverEndpoint);
            return await listener.AcceptAsync();
        }

        private INetworkConnection Connect()
        {
            IClientTransport clientTransport = new LogClientTransportDecorator(
                TestHelper.CreateClientTransport(
                    _clientEndpoint,
                    multiStreamOptions: _clientOptions),
                LogAttributeLoggerFactory.Instance.CreateLogger("IceRpc.Transports"));
            return clientTransport.CreateConnection(_clientEndpoint);
        }

        protected static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateSendPayload(IMultiplexedNetworkStream stream, int length = 10)
        {
            byte[] buffer = new byte[stream.TransportHeader.Length + length];
            stream.TransportHeader.CopyTo(buffer);
            return new ReadOnlyMemory<byte>[] { buffer };
        }

        protected static Memory<byte> CreateReceivePayload(int length = 10) => new byte[length];
    }
}
