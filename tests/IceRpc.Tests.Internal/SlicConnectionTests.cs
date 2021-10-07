// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal.Slic;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    internal class SlicConnectionProvider : MultiStreamConnectionBaseTest, IDisposable
    {
        internal new SlicConnection ClientConnection => (SlicConnection)ClientMultiStreamConnection;
        internal new SlicConnection ServerConnection => (SlicConnection)ServerMultiStreamConnection;

        public void Dispose() => TearDownConnections();

        internal SlicConnectionProvider(SlicOptions clientOptions, SlicOptions serverOptions) :
            base(clientOptions: clientOptions, serverOptions: serverOptions) =>
            SetUpConnectionsAsync().Wait();
    }

    [Timeout(5000)]
    public class SlicConnectionTests
    {
        [TestCase]
        public void SlicConnectionTests_Options()
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

            using var connections = new SlicConnectionProvider(clientOptions, serverOptions);

            Assert.That(connections.ServerConnection.PeerStreamBufferMaxSize, Is.EqualTo(2405));
            Assert.That(connections.ClientConnection.PeerStreamBufferMaxSize, Is.EqualTo(6893));
            Assert.That(connections.ServerConnection.PeerPacketMaxSize, Is.EqualTo(4567));
            Assert.That(connections.ClientConnection.PeerPacketMaxSize, Is.EqualTo(2098));
        }
    }
}
