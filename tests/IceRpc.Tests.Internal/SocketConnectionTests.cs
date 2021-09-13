// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Internal
{
    // Test the various single stream connection implementations. We don't test Ice1 + WS here as it doesn't really
    // provide additional test coverage given that the WS connection has no protocol specific code.
    [TestFixture("tcp", false)]
    [TestFixture("tcp", true)]
    [TestFixture("udp", false)]
    [Timeout(10000)]
    public class SocketConnectionTests : SocketConnectionBaseTest
    {
        public SocketConnectionTests(string transport, bool tls)
            : base(transport == "udp" ? Protocol.Ice1 : Protocol.Ice2, transport, tls)
        {
        }

        [Test]
        public void SocketConnection_Dispose()
        {
            ClientConnection.Dispose();
            ServerConnection.Dispose();
            ClientConnection.Dispose();
            ServerConnection.Dispose();
        }

        [Test]
        public void SocketConnection_Properties()
        {
            Test(ClientConnection);
            Test(ServerConnection);

            static void Test(NetworkSocket networkSocket) =>
                Assert.That(networkSocket.ToString(), Is.Not.Empty);
        }
    }
}
