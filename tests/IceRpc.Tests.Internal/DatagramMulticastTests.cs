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
        protected SingleStreamConnection OutgoingConnection => _outgoingConnection!;
        protected IList<SingleStreamConnection> IncomingConnections => _incomingConnections;
        private SingleStreamConnection? _outgoingConnection;
        private readonly int _incomingConnectionCount;
        private readonly List<SingleStreamConnection> _incomingConnections = new();

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
            _incomingConnections.Clear();
            for (int i = 0; i < _incomingConnectionCount; ++i)
            {
                _incomingConnections.Add(((MultiStreamOverSingleStreamConnection)CreateIncomingConnection()).Underlying);
            }

            ValueTask<SingleStreamConnection> connectTask = SingleStreamConnectionAsync(ConnectAsync());
            _outgoingConnection = await connectTask;
        }

        [TearDown]
        public void TearDown()
        {
            _outgoingConnection?.Dispose();
            _incomingConnections.ForEach(connection => connection.Dispose());
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
                    ValueTask sendTask = OutgoingConnection.SendAsync(sendBuffer, default);
                    foreach (SingleStreamConnection connection in IncomingConnections)
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
