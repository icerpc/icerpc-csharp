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
        protected MultiStreamConnection ClientConnection => _clientConnection!;
        protected MultiStreamConnection ServerConnection => _serverConnection!;
        protected SlicOptions ServerSlicOptions { get; }

        private readonly AsyncSemaphore _acceptSemaphore = new(1);
        private MultiStreamConnection? _clientConnection;
        private readonly Endpoint _clientEndpoint;
        // Protects the _listener data member
        private IListener? _listener;
        private readonly object _mutex = new();
        private static int _nextBasePort;
        private MultiStreamConnection? _serverConnection;
        private readonly Endpoint _serverEndpoint;

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
            Task<MultiStreamConnection> acceptTask = AcceptAsync();
            _clientConnection = await ConnectAsync();
            _serverConnection = await acceptTask;
        }

        protected void TearDownConnections()
        {
            _clientConnection?.Dispose();
            _serverConnection?.Dispose();
        }

        [OneTimeTearDown]
        public void Shutdown() => _listener?.Dispose();

        protected async Task<MultiStreamConnection> AcceptAsync()
        {
            lock (_mutex)
            {
                _listener ??= CreateListener();
            }

            await _acceptSemaphore.EnterAsync();
            try
            {
                INetworkConnection networkConnection = await _listener.AcceptAsync();
                await networkConnection.ConnectAsync(default);
                return (MultiStreamConnection)await networkConnection.GetMultiStreamConnectionAsync(default);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                throw;
            }
            finally
            {
                _acceptSemaphore.Release();
            }
        }

        protected async Task<MultiStreamConnection> ConnectAsync()
        {
            if (_clientEndpoint.Transport != "udp")
            {
                lock (_mutex)
                {
                    _listener ??= CreateListener();
                }
            }

            IClientTransport clientTransport = TestHelper.CreateClientTransport(_clientEndpoint);

            INetworkConnection networkConnection = clientTransport.CreateConnection(
                    _clientEndpoint,
                    LogAttributeLoggerFactory.Instance);
            await networkConnection.ConnectAsync(default);
                return (MultiStreamConnection)await networkConnection.GetMultiStreamConnectionAsync(default);
        }

        protected IListener CreateListener() =>
            TestHelper.CreateServerTransport(
                _serverEndpoint,
                options: null,
                slicOptions: ServerSlicOptions).Listen(
                    _serverEndpoint,
                    LogAttributeLoggerFactory.Instance).Listener!;

        protected static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateSendPayload(INetworkStream stream, int length = 10)
        {
            byte[] buffer = new byte[stream.TransportHeader.Length + length];
            stream.TransportHeader.CopyTo(buffer);
            return new ReadOnlyMemory<byte>[] { buffer };
        }

        protected static Memory<byte> CreateReceivePayload(int length = 10) => new byte[length];
    }
}
