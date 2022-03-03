// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public sealed class TransportOptionsTests
    {
        [Test]
        public void TransportOptions_UdpClientOptions()
        {
            var options = new UdpClientTransportOptions();
            Assert.That(options.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.LocalEndPoint, Is.Null);
            Assert.That(options.SendBufferSize, Is.Null);
            // Invalid settings
            Assert.Throws<ArgumentException>(() => new UdpClientTransportOptions() { IdleTimeout = TimeSpan.Zero });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new UdpClientTransportOptions() { SendBufferSize = 10 });
        }

        [Test]
        public void TransportOptions_UdpServerOptions()
        {
            var options = new UdpServerTransportOptions();
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.ReceiveBufferSize, Is.Null);
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new UdpServerTransportOptions() { ReceiveBufferSize = 10 });
        }

        [Test]
        public void TransportOptions_TcpClientOptions()
        {
            var options = new TcpClientTransportOptions();
            Assert.That(options.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.SendBufferSize, Is.Null);
            Assert.That(options.ReceiveBufferSize, Is.Null);
            Assert.That(options.LocalEndPoint, Is.Null);

            // Invalid settings
            Assert.Throws<ArgumentException>(() => new TcpClientTransportOptions() { IdleTimeout = TimeSpan.Zero });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new TcpClientTransportOptions() { ReceiveBufferSize = 10 });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new TcpClientTransportOptions() { SendBufferSize = 10 });
        }

        [Test]
        public void TransportOptions_TcpServerOptions()
        {
            var options = new TcpServerTransportOptions();
            Assert.That(options.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.SendBufferSize, Is.Null);
            Assert.That(options.ReceiveBufferSize, Is.Null);
            Assert.That(options.ListenerBackLog, Is.EqualTo(511));

            Assert.Throws<ArgumentException>(() => new TcpServerTransportOptions() { IdleTimeout = TimeSpan.Zero });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new TcpServerTransportOptions() { SendBufferSize = 10 });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new TcpServerTransportOptions() { ReceiveBufferSize = 10 });
            // Can't be less than 1
            Assert.Throws<ArgumentException>(() => new TcpServerTransportOptions() { ListenerBackLog = 0 });
        }

        [Test]
        public void TransportOptions_SlicOptions()
        {
            var options = new SlicTransportOptions();
            Assert.That(options.UnidirectionalStreamMaxCount, Is.EqualTo(100));
            Assert.That(options.BidirectionalStreamMaxCount, Is.EqualTo(100));
            Assert.That(options.PacketMaxSize, Is.EqualTo(16 * 1024));
            Assert.That(options.PauseWriterThreshold, Is.EqualTo(64 * 1024));
            Assert.That(options.ResumeWriterThreshold, Is.EqualTo(32 * 1024));

            // Can't be less than 1
            Assert.Throws<ArgumentException>(() => new SlicTransportOptions() { BidirectionalStreamMaxCount = 0 });
            Assert.Throws<ArgumentException>(() => new SlicTransportOptions() { UnidirectionalStreamMaxCount = 0 });

            // Can't be less than 1Kb
            Assert.Throws<ArgumentException>(() => new SlicTransportOptions() { PacketMaxSize = 1 });
            Assert.Throws<ArgumentException>(() => new SlicTransportOptions() { PauseWriterThreshold = 1 });
            Assert.Throws<ArgumentException>(() => new SlicTransportOptions() { ResumeWriterThreshold = 1 });
        }
    }
}
