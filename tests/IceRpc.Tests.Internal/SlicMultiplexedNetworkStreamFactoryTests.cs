// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    public class SlicMultiplexedNetworkStreamFactoryTests
    {
        [TestCase]
        public async Task SlicMultiplexedNetworkStreamFactory_Options()
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

            (SlicMultiplexedNetworkStreamFactory clientFactory,  SlicMultiplexedNetworkStreamFactory serverFactory) =
                await CreateSlicClientServerFactoriesAsync(clientOptions, serverOptions);
            try
            {
                Assert.That(serverFactory.PeerStreamBufferMaxSize, Is.EqualTo(2405));
                Assert.That(clientFactory.PeerStreamBufferMaxSize, Is.EqualTo(6893));
                Assert.That(serverFactory.PeerPacketMaxSize, Is.EqualTo(4567));
                Assert.That(clientFactory.PeerPacketMaxSize, Is.EqualTo(2098));
            }
            finally
            {
                clientFactory.Dispose();
                serverFactory.Dispose();
            }
        }

        private static async Task<(SlicMultiplexedNetworkStreamFactory, SlicMultiplexedNetworkStreamFactory)> CreateSlicClientServerFactoriesAsync(
            SlicOptions clientOptions,
            SlicOptions serverOptions)
        {
            IServerTransport serverTransport = new SlicServerTransportDecorator(
                new ColocServerTransport(),
                serverOptions,
                stream => (new StreamSlicFrameReader(stream), new StreamSlicFrameWriter(stream)));
            using IListener listener = serverTransport.Listen("ice+coloc://127.0.0.1");

            IClientTransport clientTransport = new SlicClientTransportDecorator(
                new ColocClientTransport(),
                clientOptions,
                stream => (new StreamSlicFrameReader(stream), new StreamSlicFrameWriter(stream)));

            INetworkConnection clientConnection = clientTransport.CreateConnection("ice+coloc://127.0.0.1");

            INetworkConnection serverConnection = await listener.AcceptAsync();

            Task<(IMultiplexedNetworkStreamFactory Factory, NetworkConnectionInformation Information)> clientTask =
                clientConnection.ConnectAndGetMultiplexedNetworkStreamFactoryAsync(default);
            Task<(IMultiplexedNetworkStreamFactory Factory, NetworkConnectionInformation Information)> serverTask =
                serverConnection.ConnectAndGetMultiplexedNetworkStreamFactoryAsync(default);
            return ((SlicMultiplexedNetworkStreamFactory)(await clientTask).Factory,
                    (SlicMultiplexedNetworkStreamFactory)(await serverTask).Factory);
        }
    }
}
