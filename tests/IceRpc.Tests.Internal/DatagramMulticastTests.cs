// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
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
        protected SingleStreamConnection ClientSocket => _clientSocket!;
        protected IList<SingleStreamConnection> ServerSockets => _serverSockets;
        private SingleStreamConnection? _clientSocket;
        private readonly int _incomingConnectionCount;
        private readonly List<SingleStreamConnection> _serverSockets = new();

        public DatagramMulticastTests(int incomingConnectionCount, AddressFamily addressFamily)
            : base(
                Protocol.Ice1,
                "udp",
                tls: false,
                addressFamily,
                clientEndpoint: (host, port) => GetEndpoint(host, port, addressFamily, outgoing: true),
                serverEndpoint: (host, port) => GetEndpoint(host, port, addressFamily, outgoing: false)) =>
            _incomingConnectionCount = incomingConnectionCount;

        [SetUp]
        public async Task SetupAsync()
        {
            _serverSockets.Clear();
            for (int i = 0; i < _incomingConnectionCount; ++i)
            {
                _serverSockets.Add(((MultiStreamOverSingleStreamConnection)CreateServerSocket()).Underlying);
            }

            ValueTask<SingleStreamConnection> connectTask = SingleStreamSocketAsync(ConnectAsync());
            _clientSocket = await connectTask;
        }

        [TearDown]
        public void TearDown()
        {
            _clientSocket?.Dispose();
            _serverSockets.ForEach(socket => socket.Dispose());
        }

        [TestCase(1)]
        [TestCase(1024)]
        public async Task DatagramMulticastSocket_SendReceiveAsync(int size)
        {
            var sendBuffer = new List<ArraySegment<byte>>() { new byte[size] };
            new Random().NextBytes(sendBuffer[0]);

            // Datagrams aren't reliable, try up to 5 times in case a datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask<int> sendTask = ClientSocket.SendDatagramAsync(sendBuffer, default);
                    foreach (SingleStreamConnection socket in ServerSockets)
                    {
                        ArraySegment<byte> receiveBuffer = await socket.ReceiveDatagramAsync(source.Token);
                        Assert.AreEqual(await sendTask, receiveBuffer.Count);
                        Assert.AreEqual(sendBuffer[0], receiveBuffer);
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
