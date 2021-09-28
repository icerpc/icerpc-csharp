// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Globalization;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    public class MultiStreamConnectionBaseTest
    {
        protected INetworkConnection ClientConnection => _clientConnection!;
        protected IMultiStreamConnection ClientMultiStreamConnection => _clientMultiStreamConnection!;
        protected INetworkConnection ServerConnection => _serverConnection!;
        protected IMultiStreamConnection ServerMultiStreamConnection => _serverMultiStreamConnection!;
        protected SlicOptions ServerSlicOptions { get; }

        private INetworkConnection? _clientConnection;
        private readonly Endpoint _clientEndpoint;
        private IMultiStreamConnection? _clientMultiStreamConnection;
        private static int _nextBasePort;
        private INetworkConnection? _serverConnection;
        private readonly Endpoint _serverEndpoint;
        private IMultiStreamConnection? _serverMultiStreamConnection;

        public MultiStreamConnectionBaseTest(int bidirectionalStreamMaxCount = 0, int unidirectionalStreamMaxCount = 0)
        {
            int port = 11000;
            if (TestContext.Parameters.Names.Contains("IceRpc.Tests.Internal.BasePort"))
            {
                port = int.Parse(TestContext.Parameters["IceRpc.Tests.Internal.BasePort"]!,
                                 CultureInfo.InvariantCulture.NumberFormat);
            }
            port += Interlocked.Add(ref _nextBasePort, 1);

            string endpoint = $"ice+coloc://127.0.0.1:{port}";
            // string endpoint = $"ice+tcp://127.0.0.1:{port}?tls=false";
            ServerSlicOptions = new SlicOptions();

            _serverEndpoint = endpoint;
            _clientEndpoint = endpoint;

            if (bidirectionalStreamMaxCount > 0)
            {
                ServerSlicOptions.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount;
            }
            if (unidirectionalStreamMaxCount > 0)
            {
                ServerSlicOptions.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount;
            }
        }

        protected async Task SetUpConnectionsAsync()
        {
            Task<INetworkConnection> acceptTask = AcceptAsync();
            _clientConnection = await ConnectAsync();
            _serverConnection = await acceptTask;

            ValueTask<IMultiStreamConnection> multiStreamTask = _serverConnection.GetMultiStreamConnectionAsync(default);
            _clientMultiStreamConnection = await _clientConnection.GetMultiStreamConnectionAsync(default);
            _serverMultiStreamConnection = await multiStreamTask;
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
                slicOptions: ServerSlicOptions).Listen(
                    _serverEndpoint,
                    LogAttributeLoggerFactory.Instance).Listener!;

            INetworkConnection networkConnection = await listener.AcceptAsync();
            await networkConnection.ConnectAsync(default);
            return networkConnection;
        }

        private async Task<INetworkConnection> ConnectAsync()
        {
            IClientTransport clientTransport = TestHelper.CreateClientTransport(_clientEndpoint);

            INetworkConnection networkConnection = clientTransport.CreateConnection(
                    _clientEndpoint,
                    LogAttributeLoggerFactory.Instance);
            await networkConnection.ConnectAsync(default);
            return networkConnection;
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
