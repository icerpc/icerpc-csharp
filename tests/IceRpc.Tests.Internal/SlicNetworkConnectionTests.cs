// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    public class SlicNetworkConnectionTests
    {
        [Test]
        public async Task SlicNetworkConnectionTests_Options()
        {
            var colocTransport = new ColocTransport();

            var clientOptions = new SlicClientTransportOptions
            {
                SimpleClientTransport = colocTransport.ClientTransport,
                PauseWriterThreshold = 2405,
                ResumeWriterThreshold = 2000,
                PacketMaxSize = 4567
            };
            var serverOptions = new SlicServerTransportOptions
            {
                SimpleServerTransport = colocTransport.ServerTransport,
                PauseWriterThreshold = 6893,
                ResumeWriterThreshold = 2000,
                PacketMaxSize = 2098
            };

            (SlicNetworkConnection clientConnection, SlicNetworkConnection serverConnection) =
                await CreateSlicClientServerConnectionsAsync(clientOptions, serverOptions);
            try
            {
                Assert.That(serverConnection.PeerPauseWriterThreshold, Is.EqualTo(2405));
                Assert.That(clientConnection.PeerPauseWriterThreshold, Is.EqualTo(6893));
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
            SlicClientTransportOptions clientTransportOptions,
            SlicServerTransportOptions serverTransportOptions)
        {
            IServerTransport<IMultiplexedNetworkConnection> serverTransport = new SlicServerTransport(
                serverTransportOptions);
            await using IListener<IMultiplexedNetworkConnection> listener =
                serverTransport.Listen(
                    "icerpc://127.0.0.1?transport=coloc",
                    authenticationOptions: null,
                    LogAttributeLoggerFactory.Instance.Logger);

            IClientTransport<IMultiplexedNetworkConnection> clientTransport = new SlicClientTransport(
                clientTransportOptions);
            IMultiplexedNetworkConnection clientConnection = clientTransport.CreateConnection(
                "icerpc://127.0.0.1?transport=coloc",
                authenticationOptions: null,
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
