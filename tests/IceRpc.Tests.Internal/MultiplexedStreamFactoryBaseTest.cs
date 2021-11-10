// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Tests.Internal
{
    public class MultiplexedStreamFactoryBaseTest
    {
        protected IMultiplexedNetworkConnection ClientConnection => _clientConnection!;
        protected IMultiplexedStreamFactory ClientMultiplexedStreamFactory => _clientMultiplexedStreamFactory!;
        protected IMultiplexedNetworkConnection ServerConnection => _serverConnection!;
        protected IMultiplexedStreamFactory ServerMultiplexedStreamFactory => _serverMultiplexedStreamFactory!;

        private IMultiplexedNetworkConnection? _clientConnection;
        private readonly Endpoint _clientEndpoint;
        private IMultiplexedStreamFactory? _clientMultiplexedStreamFactory;
        private readonly object? _clientOptions;
        private IMultiplexedNetworkConnection? _serverConnection;
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
            Task<IMultiplexedNetworkConnection> acceptTask = AcceptAsync();
            _clientConnection = Connect();
            _serverConnection = await acceptTask;

            Task<(IMultiplexedStreamFactory, NetworkConnectionInformation)> multiStreamTask =
                 _clientConnection.ConnectAsync(default);
            (_serverMultiplexedStreamFactory, _) = await _serverConnection.ConnectAsync(default);
            (_clientMultiplexedStreamFactory, _) = await multiStreamTask;
        }

        protected async Task TearDownConnectionsAsync()
        {
            if (_clientConnection is INetworkConnection clientConnection)
            {
                await clientConnection.DisposeAsync();
            }
            if (_clientConnection is INetworkConnection serverConnection)
            {
                await serverConnection.DisposeAsync();
            }
        }

        private async Task<IMultiplexedNetworkConnection> AcceptAsync()
        {
            await using IListener<IMultiplexedNetworkConnection> listener =
                TestHelper.CreateMultiplexedServerTransport(
                    _serverEndpoint.Transport,
                    options: null,
                    slicOptions: _serverOptions as SlicOptions).Listen(_serverEndpoint,
                                                                       LogAttributeLoggerFactory.Instance.Server);
            return await listener.AcceptAsync();
        }

        private IMultiplexedNetworkConnection Connect()
        {
            IClientTransport<IMultiplexedNetworkConnection> clientTransport =
                TestHelper.CreateMultiplexedClientTransport(
                    _clientEndpoint.Transport,
                    slicOptions: _clientOptions as SlicOptions);
            return clientTransport.CreateConnection(_clientEndpoint, LogAttributeLoggerFactory.Instance.Client);
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
