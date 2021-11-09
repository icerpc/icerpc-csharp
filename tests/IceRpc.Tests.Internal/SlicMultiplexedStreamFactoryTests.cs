// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    public class SlicMultiplexedStreamFactoryTests
    {
        [TestCase]
        public async Task SlicMultiplexedStreamFactoryTests_Options()
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

            (SlicMultiplexedStreamFactory clientConnection, SlicMultiplexedStreamFactory serverConnection) =
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
                clientConnection.Dispose();
                serverConnection.Dispose();
            }
        }

        private static async Task<(SlicMultiplexedStreamFactory, SlicMultiplexedStreamFactory)> CreateSlicClientServerConnectionsAsync(
            SlicOptions clientOptions,
            SlicOptions serverOptions)
        {
            IServerTransport<IMultiplexedNetworkConnection> serverTransport =
                new SlicServerTransport(new ColocServerTransport(), serverOptions);
            using IListener<IMultiplexedNetworkConnection> listener =
                serverTransport.Listen("ice+coloc://127.0.0.1", LogAttributeLoggerFactory.Instance.Server);

            IClientTransport<IMultiplexedNetworkConnection> clientTransport =
                new SlicClientTransport(new ColocClientTransport(), clientOptions);
            IMultiplexedNetworkConnection clientConnection = clientTransport.CreateConnection(
                "ice+coloc://127.0.0.1",
                LogAttributeLoggerFactory.Instance.Client);

            IMultiplexedNetworkConnection serverConnection = await listener.AcceptAsync();
            Task<(IMultiplexedStreamFactory Factory, NetworkConnectionInformation Information)> clientTask =
                clientConnection.ConnectAsync(default);
            Task<(IMultiplexedStreamFactory Factory, NetworkConnectionInformation Information)> serverTask =
                serverConnection.ConnectAsync(default);
            return ((SlicMultiplexedStreamFactory)(await clientTask).Factory,
                    (SlicMultiplexedStreamFactory)(await serverTask).Factory);
        }
    }
}
