// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    public class ConnectionTests
    {
        [Test]
        public void Connection_Options_ArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new ConnectionOptions { CloseTimeout = TimeSpan.Zero });
            Assert.Throws<ArgumentException>(() => new ConnectionOptions { IncomingFrameMaxSize = 512 });
            Assert.Throws<ArgumentException>(() => new ConnectionOptions { ConnectTimeout = TimeSpan.Zero });
        }

        [Test]
        public void Connection_TcpServerTransportOptions_ArgumentException()
        {
            var options = new TcpServerOptions();
            Assert.Throws<ArgumentException>(() => new TcpServerOptions() { ListenerBackLog = 0 });
            Assert.Throws<ArgumentException>(() => new TcpServerOptions() { SendBufferSize = 512 });
            Assert.Throws<ArgumentException>(() => new TcpServerOptions() { ReceiveBufferSize = 512 });
        }

        [Test]
        public void Connection_SlicTransportOptions_ArgumentException()
        {
            var options = new SlicOptions();
            Assert.Throws<ArgumentException>(() => new SlicOptions() { PacketMaxSize = 512 });
            Assert.Throws<ArgumentException>(() => new SlicOptions() { StreamBufferMaxSize = 512 });
        }

        [Test]
        public void Connection_UdpClientTransportOptions_ArgumentException()
        {
            var options = new UdpClientOptions();
            Assert.Throws<ArgumentException>(() => new UdpClientOptions() { SendBufferSize = 512 });
        }

        [Test]
        public void Connection_UdpServerTransportOptions_ArgumentException()
        {
            var options = new UdpServerOptions();
            Assert.Throws<ArgumentException>(() => new UdpServerOptions() { ReceiveBufferSize = 512 });
        }
    }
}
