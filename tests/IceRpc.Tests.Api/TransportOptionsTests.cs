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
            var options = new UdpClientOptions();
            Assert.That(options.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.LocalEndPoint, Is.Null);
            Assert.That(options.SendBufferSize, Is.Null);
            // Invalid settings
            Assert.Throws<ArgumentException>(() => new UdpClientOptions() { IdleTimeout = TimeSpan.Zero });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new UdpClientOptions() { SendBufferSize = 10 });
        }

        [Test]
        public void TransportOptions_UdpServerOptions()
        {
            var options = new UdpServerOptions();
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.ReceiveBufferSize, Is.Null);
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new UdpServerOptions() { ReceiveBufferSize = 10 });
        }

        [Test]
        public void TransportOptions_TcpClientOptions()
        {
            var options = new TcpClientOptions();
            Assert.That(options.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.SendBufferSize, Is.Null);
            Assert.That(options.ReceiveBufferSize, Is.Null);
            Assert.That(options.LocalEndPoint, Is.Null);

            // Invalid settings
            Assert.Throws<ArgumentException>(() => new TcpClientOptions() { IdleTimeout = TimeSpan.Zero });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new TcpClientOptions() { ReceiveBufferSize = 10 });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new TcpClientOptions() { SendBufferSize = 10 });
        }

        [Test]
        public void TransportOptions_TcpServerOptions()
        {
            var options = new TcpServerOptions();
            Assert.That(options.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(options.IsIPv6Only, Is.False);
            Assert.That(options.SendBufferSize, Is.Null);
            Assert.That(options.ReceiveBufferSize, Is.Null);
            Assert.That(options.ListenerBackLog, Is.EqualTo(511));

            Assert.Throws<ArgumentException>(() => new TcpServerOptions() { IdleTimeout = TimeSpan.Zero });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new TcpServerOptions() { SendBufferSize = 10 });
            // Buffer size must be at least 1KB
            Assert.Throws<ArgumentException>(() => new TcpServerOptions() { ReceiveBufferSize = 10 });
            // Can't be less than 1
            Assert.Throws<ArgumentException>(() => new TcpServerOptions() { ListenerBackLog = 0 });
        }

        [Test]
        public void TransportOptions_SlicOptions()
        {
            var options = new SlicOptions();
            Assert.That(options.UnidirectionalStreamMaxCount, Is.EqualTo(100));
            Assert.That(options.BidirectionalStreamMaxCount, Is.EqualTo(100));
            Assert.That(options.PacketMaxSize, Is.EqualTo(16 * 1024));
            Assert.That(options.PauseWriterThreshold, Is.EqualTo(64 * 1024));
            Assert.That(options.ResumeWriterThreshold, Is.EqualTo(32 * 1024));

            // Can't be less than 1
            Assert.Throws<ArgumentException>(() => new SlicOptions() { BidirectionalStreamMaxCount = 0 });
            Assert.Throws<ArgumentException>(() => new SlicOptions() { UnidirectionalStreamMaxCount = 0 });

            // Can't be less than 1Kb
            Assert.Throws<ArgumentException>(() => new SlicOptions() { PacketMaxSize = 1 });
            Assert.Throws<ArgumentException>(() => new SlicOptions() { PauseWriterThreshold = 1 });
            Assert.Throws<ArgumentException>(() => new SlicOptions() { ResumeWriterThreshold = 1 });
        }
    }
}
