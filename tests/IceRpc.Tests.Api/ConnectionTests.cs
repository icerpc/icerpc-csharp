// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;

namespace IceRpc.Tests.Api
{
    public class ConnectionTests
    {
        [Test]
        public void Connection_Options_ArgumentException()
        {
            var options = new ClientConnectionOptions();
            Assert.Throws<ArgumentException>(() => options.BidirectionalStreamMaxCount = 0);
            Assert.Throws<ArgumentException>(() => options.CloseTimeout = TimeSpan.Zero);
            Assert.Throws<ArgumentException>(() => options.IdleTimeout = TimeSpan.Zero);
            Assert.Throws<ArgumentException>(() => options.IncomingFrameMaxSize = 512);
            Assert.Throws<ArgumentException>(() => options.UnidirectionalStreamMaxCount = 0);

            Assert.Throws<ArgumentException>(() => options.ConnectTimeout = TimeSpan.Zero);

            var incoming = new ServerConnectionOptions();
            Assert.Throws<ArgumentException>(() => incoming.AcceptTimeout = TimeSpan.Zero);
        }

        [Test]
        public void Connection_Options_Clone()
        {
            var options = new ClientConnectionOptions
            {
                TransportOptions = new TcpOptions(),
                AuthenticationOptions = new System.Net.Security.SslClientAuthenticationOptions()
            };

            ClientConnectionOptions clonedOptions = options.Clone();
            Assert.AreNotSame(clonedOptions, options);
            Assert.AreNotSame(clonedOptions.TransportOptions, options.TransportOptions);
            Assert.AreNotSame(clonedOptions.AuthenticationOptions, options.AuthenticationOptions);

            var incoming = new ServerConnectionOptions()
            {
                AuthenticationOptions = new()
            };

            ServerConnectionOptions clonedIncoming = incoming.Clone();
            Assert.AreNotSame(clonedIncoming.AuthenticationOptions, incoming.AuthenticationOptions);
        }

        [Test]
        public void Connection_TcpOptions_ArgumentException()
        {
            var options = new TcpOptions();
            Assert.Throws<ArgumentException>(() => options.ListenerBackLog = 0);
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 512);
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 512);
            Assert.Throws<ArgumentException>(() => options.SlicPacketMaxSize = 512);
            Assert.Throws<ArgumentException>(() => options.SlicStreamBufferMaxSize = 512);
        }

        [Test]
        public void Connection_UdpOptions_ArgumentException()
        {
            var options = new UdpOptions();
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 512);
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 512);
        }
    }
}
