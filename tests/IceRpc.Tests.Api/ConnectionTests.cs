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
            var options = new Transports.TcpServerOptions();
            Assert.Throws<ArgumentException>(() => options.ListenerBackLog = 0);
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 512);
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 512);
        }

        [Test]
        public void Connection_SlicTransportOptions_ArgumentException()
        {
            var options = new Transports.SlicOptions();
            Assert.Throws<ArgumentException>(() => options.PacketMaxSize = 512);
            Assert.Throws<ArgumentException>(() => options.StreamBufferMaxSize = 512);
        }

        [Test]
        public void Connection_UdpClientTransportOptions_ArgumentException()
        {
            var options = new Transports.UdpClientOptions();
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 512);
        }

        [Test]
        public void Connection_UdpServerTransportOptions_ArgumentException()
        {
            var options = new Transports.UdpServerOptions();
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 512);
        }
    }
}
