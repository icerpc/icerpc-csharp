// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    public class ConnectionTests
    {
        [Test]
        public void Connection_Options_ArgumentException()
        {
            var options = new OutgoingConnectionOptions();
            Assert.Throws<ArgumentException>(() => options.BidirectionalStreamMaxCount = 0);
            Assert.Throws<ArgumentException>(() => options.CloseTimeout = TimeSpan.Zero);
            Assert.Throws<ArgumentException>(() => options.IdleTimeout = TimeSpan.Zero);
            Assert.Throws<ArgumentException>(() => options.IncomingFrameMaxSize = 512);
            Assert.Throws<ArgumentException>(() => options.UnidirectionalStreamMaxCount = 0);

            Assert.Throws<ArgumentException>(() => options.ConnectTimeout = TimeSpan.Zero);

            var incoming = new IncomingConnectionOptions();
            Assert.Throws<ArgumentException>(() => incoming.AcceptTimeout = TimeSpan.Zero);
        }

        [Test]
        public void Connection_Options_Clone()
        {
            var options = new OutgoingConnectionOptions()
            {
                SlicOptions = new SlicOptions(),
                SocketOptions = new SocketOptions(),
                AuthenticationOptions = new System.Net.Security.SslClientAuthenticationOptions()
            };
            var clonedOptions = options.Clone();

            Assert.AreNotSame(clonedOptions, options);
            Assert.AreNotSame(clonedOptions.SlicOptions, options.SlicOptions);
            Assert.AreNotSame(clonedOptions.SocketOptions, options.SocketOptions);
            Assert.AreNotSame(clonedOptions.AuthenticationOptions, options.AuthenticationOptions);

            var incoming = new IncomingConnectionOptions()
            {
                AuthenticationOptions = new()
            };
            var clonedIncoming = incoming.Clone();

            Assert.AreNotSame(clonedIncoming.AuthenticationOptions, incoming.AuthenticationOptions);
        }

        [Test]
        public void Connection_SlicOptions_ArgumentException()
        {
            var options = new SlicOptions();
            Assert.Throws<ArgumentException>(() => options.PacketMaxSize = 512);
            Assert.Throws<ArgumentException>(() => options.StreamBufferMaxSize = 512);
        }

        [Test]
        public void Connection_SocketOptions_ArgumentException()
        {
            var options = new SocketOptions();
            Assert.Throws<ArgumentException>(() => options.ListenerBackLog = 0);
            Assert.Throws<ArgumentException>(() => options.SendBufferSize = 512);
            Assert.Throws<ArgumentException>(() => options.ReceiveBufferSize = 512);
        }
    }
}
