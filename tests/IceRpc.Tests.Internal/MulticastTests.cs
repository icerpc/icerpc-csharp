// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [TestFixture(1, AddressFamily.InterNetwork)]
    [TestFixture(1, AddressFamily.InterNetworkV6)]
    [TestFixture(5, AddressFamily.InterNetwork)]
    [TestFixture(5, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class MulticastTests : NetworkSocketBaseTest
    {
        protected NetworkSocket ClientConnection => _clientSocket!;
        protected IList<NetworkSocket> ServerConnections => _serverSockets;
        private NetworkSocket? _clientSocket;
        private readonly int _serverConnectionCount;
        private readonly List<NetworkSocket> _serverSockets = new();

        public MulticastTests(int serverConnectionCount, AddressFamily addressFamily)
            : base(
                "udp",
                tls: false,
                addressFamily,
                clientEndpoint: (host, port) => GetEndpoint(host, port, addressFamily, client: true),
                serverEndpoint: (host, port) => GetEndpoint(host, port, addressFamily, client: false)) =>
            _serverConnectionCount = serverConnectionCount;

        [SetUp]
        public async Task SetupAsync()
        {
            _serverSockets.Clear();
            for (int i = 0; i < _serverConnectionCount; ++i)
            {
                _serverSockets.Add(CreateServerNetworkSocket());
            }
            _clientSocket = await ConnectAsync();
        }

        [TearDown]
        public void TearDown()
        {
            _clientSocket?.Dispose();
            _serverSockets.ForEach(connection => connection.Dispose());
        }

        [TestCase(1)]
        [TestCase(1024)]
        public async Task DatagramMulticast_SendReceiveAsync(int size)
        {
            var sendBuffer = new byte[size];
            new Random().NextBytes(sendBuffer);

            // Datagrams aren't reliable, try up to 5 times in case a datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask sendTask = ClientConnection.SendAsync(sendBuffer, default);
                    foreach (NetworkSocket connection in ServerConnections)
                    {
                        Memory<byte> receiveBuffer = new byte[connection.DatagramMaxReceiveSize];
                        int received = await connection.ReceiveAsync(receiveBuffer, source.Token);
                        Assert.AreEqual(sendBuffer.Length, received);
                        for (int i = 0; i < received; ++i)
                        {
                            Assert.AreEqual(sendBuffer[i], receiveBuffer.Span[i]);
                        }
                    }
                    break;
                }
                catch (OperationCanceledException)
                {
                }
            }
            Assert.AreNotEqual(0, count);
        }

        private static string GetEndpoint(string host, int port, AddressFamily addressFamily, bool client)
        {
            bool ipv6 = addressFamily == AddressFamily.InterNetworkV6;
            string address = ipv6 ? (OperatingSystem.IsLinux() ? "\"ff15::1\"" : "\"ff02::1\"") : "239.255.1.1";
            string endpoint = $"udp -h {address} -p {port}";
            if (client && !OperatingSystem.IsLinux())
            {
                endpoint += $" --interface {host}";
            }
            return endpoint;
        }
    }
}
