// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    public class SlicMultiplexedNetworkConnectionTests
    {
        [TestCase]
        public async Task SlicMultiplexedNetworkConnectionTests_Options()
        {
            var clientOptions = new SlicOptions
            {
                StreamBufferMaxSize = 2405,
                PacketMaxSize = 4567
            };
            var serverOptions = new SlicOptions
            {
                StreamBufferMaxSize = 6893,
                PacketMaxSize = 2098
            };

            (SlicNetworkConnection clientConnection, SlicNetworkConnection serverConnection) =
                await CreateSlicClientServerConnectionsAsync(clientOptions, serverOptions);
            try
            {
                Assert.That(serverConnection.PeerStreamBufferMaxSize, Is.EqualTo(2405));
                Assert.That(clientConnection.PeerStreamBufferMaxSize, Is.EqualTo(6893));
                Assert.That(serverConnection.PeerPacketMaxSize, Is.EqualTo(4567));
                Assert.That(clientConnection.PeerPacketMaxSize, Is.EqualTo(2098));
            }
            finally
            {
                await clientConnection.DisposeAsync();
                await serverConnection.DisposeAsync();
            }
        }

        private static async Task<(SlicNetworkConnection, SlicNetworkConnection)> CreateSlicClientServerConnectionsAsync(
            SlicOptions clientOptions,
            SlicOptions serverOptions)
        {
            IServerTransport<IMultiplexedNetworkConnection> serverTransport =
                new SlicServerTransport(new ColocServerTransport(), serverOptions);
            await using IListener<IMultiplexedNetworkConnection> listener =
                serverTransport.Listen("ice+coloc://127.0.0.1", LogAttributeLoggerFactory.Instance.Logger);

            IClientTransport<IMultiplexedNetworkConnection> clientTransport =
                new SlicClientTransport(new ColocClientTransport(), clientOptions);
            IMultiplexedNetworkConnection clientConnection = clientTransport.CreateConnection(
                "ice+coloc://127.0.0.1",
                LogAttributeLoggerFactory.Instance.Logger);

            IMultiplexedNetworkConnection serverConnection = await listener.AcceptAsync();
            Task<NetworkConnectionInformation> clientTask = clientConnection.ConnectAsync(default);
            Task<NetworkConnectionInformation> serverTask = serverConnection.ConnectAsync(default);
            _ = await clientTask;
            _ = await serverTask;
            return ((SlicNetworkConnection)clientConnection, (SlicNetworkConnection)serverConnection);
        }
    }
}
