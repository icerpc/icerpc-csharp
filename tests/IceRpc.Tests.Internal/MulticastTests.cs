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

        [TestCase(1, 1)]
        [TestCase(1024, 1)]
        [TestCase(1, 5)]
        [TestCase(1024, 5)]
        public async Task Multicast_ReadWriteAsync(int size, int serverCount)
        {
            byte[] writeBuffer = new byte[size];
            new Random().NextBytes(writeBuffer);
            var sendBuffers = new List<ReadOnlyMemory<byte>>() { writeBuffer };

            string host = _ipv6 ? "[::1]" : "127.0.0.1";
            Endpoint serverEndpoint = GetEndpoint(host, port: 0, _ipv6, client: false);

            var listenerList = new List<IListener<ISimpleNetworkConnection>>();
            var serverConnectionList = new List<ISimpleNetworkConnection>();

            IListener<ISimpleNetworkConnection> listener =
                _serverTransport.Listen(serverEndpoint, LogAttributeLoggerFactory.Instance.Logger);
            listenerList.Add(listener);

            serverEndpoint = serverEndpoint with { Port = listener.Endpoint.Port };

            // We create serverCount servers all listening on the same endpoint (including same port)
            for (int i = 0; i < serverCount; ++i)
            {
                if (i > 0)
                {
                    listener = _serverTransport.Listen(serverEndpoint, LogAttributeLoggerFactory.Instance.Logger);
                }

                ISimpleNetworkConnection serverConnection = await listener.AcceptAsync();
                serverConnectionList.Add(serverConnection);
                _ = await serverConnection.ConnectAsync(default);
            }

            string clientEndpoint = GetEndpoint(host, port: serverEndpoint.Port, _ipv6, client: true);

            await using ISimpleNetworkConnection clientConnection =
                _clientTransport.CreateConnection(clientEndpoint, LogAttributeLoggerFactory.Instance.Logger);

            _ = await clientConnection.ConnectAsync(default);

            // Datagrams aren't reliable, try up to 5 times in case a datagram is lost.
            int count = 5;
            while (count-- > 0)
            {
                try
                {
                    using var source = new CancellationTokenSource(1000);
                    ValueTask writeTask = clientConnection.WriteAsync(sendBuffers, default);

                    foreach (ISimpleNetworkConnection serverConnection in serverConnectionList)
                    {
                        Memory<byte> readBuffer = new byte[UdpUtils.MaxPacketSize];
                        int received = await serverConnection.ReadAsync(readBuffer, source.Token);
                        Assert.That(received, Is.EqualTo(writeBuffer.Length));
                        for (int i = 0; i < received; ++i)
                        {
                            Assert.That(readBuffer.Span[i], Is.EqualTo(writeBuffer[i]));
                        }
                    }
                    break; // done
                }
                catch (OperationCanceledException)
                {
                }
            }
            Assert.That(count, Is.Not.EqualTo(0));

            await Task.WhenAll(listenerList.Select(listener => listener.DisposeAsync().AsTask()));
            await Task.WhenAll(serverConnectionList.Select(connection => connection.DisposeAsync().AsTask()));
        }

        private static string GetEndpoint(string host, int port, bool ipv6, bool client)
        {
            string address = ipv6 ? (OperatingSystem.IsLinux() ? "[ff15::1]" : "[ff02::1]") : "239.255.1.1";
            string endpoint = $"ice://{address}:{port}?transport=udp";
            if (client && !OperatingSystem.IsLinux())
            {
                endpoint += $"&interface={host}";
            }
            return endpoint;
        }
    }
}
