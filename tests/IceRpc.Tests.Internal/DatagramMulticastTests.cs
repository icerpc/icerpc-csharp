// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    [TestFixture(1, AddressFamily.InterNetwork)]
    [TestFixture(1, AddressFamily.InterNetworkV6)]
    [TestFixture(5, AddressFamily.InterNetwork)]
    [TestFixture(5, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class DatagramMulticastTests : ConnectionBaseTest
    {
        protected NetworkSocket ClientConnection => _clientConnection!;
        protected IList<NetworkSocket> ServerConnections => _serverConnections;
        private NetworkSocket? _clientConnection;
        private readonly int _serverConnectionCount;
        private readonly List<NetworkSocket> _serverConnections = new();

        public DatagramMulticastTests(int serverConnectionCount, AddressFamily addressFamily)
            : base(
                Protocol.Ice1,
                "udp",
                tls: false,
                addressFamily,
                clientEndpoint: (host, port) => GetEndpoint(host, port, addressFamily, outgoing: true),
                serverEndpoint: (host, port) => GetEndpoint(host, port, addressFamily, outgoing: false)) =>
            _serverConnectionCount = serverConnectionCount;

        [SetUp]
        public async Task SetupAsync()
        {
            _serverConnections.Clear();
            for (int i = 0; i < _serverConnectionCount; ++i)
            {
                _serverConnections.Add(((NetworkSocketConnection)CreateServerConnection()).NetworkSocket);
            }

            ValueTask<NetworkSocket> connectTask = NetworkSocketConnectionAsync(ConnectAsync());
            _clientConnection = await connectTask;
        }

        [TearDown]
        public void TearDown()
        {
            _clientConnection?.Dispose();
            _serverConnections.ForEach(connection => connection.Dispose());
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

        private static string GetEndpoint(string host, int port, AddressFamily addressFamily, bool outgoing)
        {
            bool ipv6 = addressFamily == AddressFamily.InterNetworkV6;
            string address = ipv6 ? (OperatingSystem.IsLinux() ? "\"ff15::1\"" : "\"ff02::1\"") : "239.255.1.1";
            string endpoint = $"udp -h {address} -p {port}";
            if (outgoing && !OperatingSystem.IsLinux())
            {
                endpoint += $" --interface {host}";
            }
            return endpoint;
        }
    }
}
