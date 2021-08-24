// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    public class ConnectionTests
    {
        [Test]
        public void Connection_Options_ArgumentException()
        {
            var options = new ConnectionOptions();
            Assert.Throws<ArgumentException>(() => options.CloseTimeout = TimeSpan.Zero);
            Assert.Throws<ArgumentException>(() => options.IdleTimeout = TimeSpan.Zero);
            Assert.Throws<ArgumentException>(() => options.IncomingFrameMaxSize = 512);

            Assert.Throws<ArgumentException>(() => options.ConnectTimeout = TimeSpan.Zero);
        }

        [Test]
        public void Connection_TcpTransportOptions_ArgumentException()
        {
            var options = new Transports.TcpOptions();
            Assert.Throws<ArgumentException>(() => options.ListenerBackLog = 0);
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 512);
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 512);
            Assert.Throws<ArgumentException>(() => options.SlicPacketMaxSize = 512);
            Assert.Throws<ArgumentException>(() => options.SlicStreamBufferMaxSize = 512);
        }

        [Test]
        public void Connection_UdpTransportOptions_ArgumentException()
        {
            var options = new Transports.UdpOptions();
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 512);
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 512);
        }
    }
}
