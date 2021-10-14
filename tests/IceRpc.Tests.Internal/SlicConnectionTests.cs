// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    [Timeout(5000)]
    public class SlicConnectionTests
    {
        [TestCase]
        public async Task SlicConnectionTests_Options()
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

            (SlicConnection clientConnection,  SlicConnection serverConnection) =
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

        private static async Task<(SlicConnection, SlicConnection)> CreateSlicClientServerConnectionsAsync(
            SlicOptions clientOptions,
            SlicOptions serverOptions)
        {
            IServerTransport serverTransport = new ColocServerTransport(serverOptions);
            using IListener listener = serverTransport.Listen("ice+coloc://127.0.0.1");

            IClientTransport clientTransport = new ColocClientTransport(clientOptions);
            INetworkConnection clientConnection = clientTransport.CreateConnection("ice+coloc://127.0.0.1");

            INetworkConnection serverConnection = await listener.AcceptAsync();
            ValueTask<(IMultiStreamConnection Connection, NetworkConnectionInformation Information)> clientTask =
                clientConnection.ConnectMultiStreamConnectionAsync(default);
            ValueTask<(IMultiStreamConnection Connection, NetworkConnectionInformation Information)> serverTask =
                serverConnection.ConnectMultiStreamConnectionAsync(default);
            return ((SlicConnection)(await clientTask).Connection, (SlicConnection)(await serverTask).Connection);
        }
    }
}
