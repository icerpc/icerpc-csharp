// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
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
            var options = new OutgoingConnectionOptions
            {
                TransportOptions = new TcpOptions(),
                AuthenticationOptions = new System.Net.Security.SslClientAuthenticationOptions()
            };

            OutgoingConnectionOptions clonedOptions = options.Clone();
            Assert.AreNotSame(clonedOptions, options);
            Assert.AreNotSame(clonedOptions.TransportOptions, options.TransportOptions);
            Assert.AreNotSame(clonedOptions.AuthenticationOptions, options.AuthenticationOptions);

            var incoming = new IncomingConnectionOptions()
            {
                AuthenticationOptions = new()
            };

            IncomingConnectionOptions clonedIncoming = incoming.Clone();
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

        [Test]
        public async Task Connection_DispatcherAsync()
        {
            var router = new Router();
            router.Use(next => new InlineDispatcher(async (request, cancel) =>
            {
                await IGreeterServicePrx.FromConnection(request.Connection).IcePingAsync(cancel: cancel);
                return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
            }));
            router.Map<IGreeterService>(new GreeterService());

            await using var server = new Server
            {
                Endpoint = "ice+coloc://connection_dispatcher:1000",
                Dispatcher = router
            };
            server.Listen();

            router = new Router();
            IncomingRequest? bidirRequest = null;
            router.Use(next => new InlineDispatcher((request, cancel) =>
            {
                bidirRequest = request;
                return new(new OutgoingResponse(request, Payload.FromVoidReturnValue(request)));
            }));
            router.Map<IGreeterService>(new GreeterService());

            await using var connection = new Connection
            {
                RemoteEndpoint = server.ProxyEndpoint,
                Dispatcher = router
            };

            var prx = IGreeterServicePrx.FromConnection(connection);
            await prx.IcePingAsync();
            Assert.IsNotNull(bidirRequest);
            Assert.AreEqual("ice_ping", bidirRequest!.Operation);
        }

        public class GreeterService : IGreeterService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                default;
        }
    }
}
