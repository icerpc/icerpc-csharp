// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Tests.Internal
{
    public class MultiplexedNetworkConnectionBaseTest
    {
        protected IMultiplexedNetworkConnection ClientConnection => _clientConnection!;
        protected IMultiplexedNetworkConnection ServerConnection => _serverConnection!;

        private IMultiplexedNetworkConnection? _clientConnection;
        private readonly Endpoint _clientEndpoint;
        private readonly object? _clientOptions;
        private IMultiplexedNetworkConnection? _serverConnection;
        private readonly Endpoint _serverEndpoint;
        private readonly object? _serverOptions;

        public MultiplexedNetworkConnectionBaseTest(
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

            Task<NetworkConnectionInformation> connectTask = _clientConnection.ConnectAsync(default);
            _ = await _serverConnection.ConnectAsync(default);
            _ = await connectTask;
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
                                                                       LogAttributeLoggerFactory.Instance.Logger);
            return await listener.AcceptAsync();
        }

        private IMultiplexedNetworkConnection Connect()
        {
            IClientTransport<IMultiplexedNetworkConnection> clientTransport =
                TestHelper.CreateMultiplexedClientTransport(
                    _clientEndpoint.Transport,
                    slicOptions: _clientOptions as SlicOptions);
            return clientTransport.CreateConnection(_clientEndpoint, LogAttributeLoggerFactory.Instance.Logger);
        }
    }
}
