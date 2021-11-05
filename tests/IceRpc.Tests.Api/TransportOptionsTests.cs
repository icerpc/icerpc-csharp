// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    [Parallelizable(scope: ParallelScope.All)]
    public sealed class TransportOptionsTests
    {
        [Test]
        public void TransportOptions_UdpClientOptions()
        {
            var options = new UdpClientOptions();
            Assert.AreEqual(options.IdleTimeout, TimeSpan.FromSeconds(60));
            Assert.Throws<ArgumentException>(() => options.IdleTimeout = TimeSpan.Zero);
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.LocalEndPoint, Is.Null);
            Assert.That(options.SendBufferSize, Is.Null);
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 10);
        }

        [Test]
        public void TransportOptions_UdpServerOptions()
        {
            var options = new UdpServerOptions();

            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.ReceiveBufferSize, Is.Null);
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 10);
        }

        [Test]
        public void TransportOptions_TcpClientOptions()
        {
            var options = new TcpClientOptions();
            Assert.AreEqual(options.IdleTimeout, TimeSpan.FromSeconds(60));
            Assert.Throws<ArgumentException>(() => options.IdleTimeout = TimeSpan.Zero);
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.SendBufferSize, Is.Null);
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 10);
            Assert.That(options.ReceiveBufferSize, Is.Null);
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 10);
            Assert.That(options.AuthenticationOptions, Is.Null);
            Assert.That(options.LocalEndPoint, Is.Null);
        }

        [Test]
        public void TransportOptions_TcpServerOptions()
        {
            var options = new TcpServerOptions();
            Assert.AreEqual(options.IdleTimeout, TimeSpan.FromSeconds(60));
            Assert.Throws<ArgumentException>(() => options.IdleTimeout = TimeSpan.Zero);
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.SendBufferSize, Is.Null);
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 10);
            Assert.That(options.ReceiveBufferSize, Is.Null);
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 10);
            Assert.That(options.AuthenticationOptions, Is.Null);
            Assert.AreEqual(511, options.ListenerBackLog);
            // Can't be less than 1
            Assert.Throws<ArgumentException>(() => options.ListenerBackLog = 0);
        }

        [Test]
        public void TransportOptions_SlicOptions()
        {
            var options = new SlicOptions();
            Assert.AreEqual(100, options.BidirectionalStreamMaxCount);
            // Can't be less than 1
            Assert.Throws<ArgumentException>(() => options.BidirectionalStreamMaxCount = 0);

            Assert.AreEqual(32 * 1024, options.PacketMaxSize);
            // Can't be less than 1Kb
            Assert.Throws<ArgumentException>(() => options.PacketMaxSize = 1);

            // The StreamBufferMaxSize default is twice the PacketSize
            Assert.AreEqual(2 * options.PacketMaxSize, options.StreamBufferMaxSize);
            // Can't be less than 1KB
            Assert.Throws<ArgumentException>(() => options.StreamBufferMaxSize = 1);

            Assert.AreEqual(100, options.UnidirectionalStreamMaxCount);
            // Can't be less than 1
            Assert.Throws<ArgumentException>(() => options.UnidirectionalStreamMaxCount = 0);
        }
    }
}
