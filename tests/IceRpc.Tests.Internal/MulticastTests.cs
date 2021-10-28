// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Net.Sockets;

namespace IceRpc.Tests.Internal
{
    [Parallelizable(scope: ParallelScope.Fixtures)]
    [TestFixture(AddressFamily.InterNetwork)]
    [TestFixture(AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class MulticastTests
    {
        private static readonly IClientTransport<ISimpleNetworkConnection> _clientTransport = new UdpClientTransport();
        private static readonly IServerTransport<ISimpleNetworkConnection> _serverTransport = new UdpServerTransport();

        private readonly bool _ipv6;

        public MulticastTests(AddressFamily addressFamily) => _ipv6 = addressFamily == AddressFamily.InterNetworkV6;

        [TestCase(1)]
        [TestCase(1024)]
        public async Task Multicast_ReadWriteAsync(int size)
        {
            byte[] writeBuffer = new byte[size];
            new Random().NextBytes(writeBuffer);
            ReadOnlyMemory<ReadOnlyMemory<byte>> sendBuffers = new ReadOnlyMemory<byte>[] { writeBuffer };

            string host = _ipv6 ? "\"::1\"" : "127.0.0.1";
            string serverEndpoint = GetEndpoint(host, port: 0, _ipv6, client: false);

            using IListener<ISimpleNetworkConnection> listener =
                _serverTransport.Listen(serverEndpoint, LogAttributeLoggerFactory.Instance);

            ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();
            (ISimpleStream serverStream, _) = await serverConnection.ConnectAsync(default);

            string clientEndpoint = GetEndpoint(host, port: listener.Endpoint.Port, _ipv6, client: true);

            ISimpleNetworkConnection clientConnection =
                _clientTransport.CreateConnection(clientEndpoint, LogAttributeLoggerFactory.Instance);

            (ISimpleStream clientStream, _) = await clientConnection.ConnectAsync(default);

            // Datagrams aren't reliable, try up to 5 times in case a datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask sendTask = clientStream.WriteAsync(sendBuffers, default);

                    Memory<byte> readBuffer = new byte[serverStream.DatagramMaxReceiveSize];
                    int received = await serverStream.ReadAsync(readBuffer, source.Token);
                    Assert.AreEqual(writeBuffer.Length, received);
                    for (int i = 0; i < received; ++i)
                    {
                        Assert.AreEqual(writeBuffer[i], readBuffer.Span[i]);
                    }
                    break; // done
                }
                catch (OperationCanceledException)
                {
                }
            }
            Assert.AreNotEqual(0, count);

            clientConnection.Close();
            serverConnection.Close();
        }

        private static string GetEndpoint(string host, int port, bool ipv6, bool client)
        {
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
