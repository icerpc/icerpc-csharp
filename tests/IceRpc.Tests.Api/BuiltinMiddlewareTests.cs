// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{

    [Parallelizable]
    public class BuiltinMiddlewareTests
    {
        private struct LogState
        {
            public string Message { get; set; }
            public long StreamId { get; set; }
            public string Operation { get; set; }
            public string Path { get; set; }
            public string Peer { get; set; }
            public string Status { get; set; }
            public double Duration { get; set; }
        }
        private struct LogMessage
        {
            public int EventId { get; set; }
            public string LogLevel { get; set; }
            public string Category { get; set; }
            public string Message { get; set; }
            public LogState State { get; set; }
            public string[] Scopes { get; set; }
        }

        /// <summary>Test logger middleware</summary>
        [Test]
        public async Task BuiltinMiddleware_LoggerMessage()
        {
            using var writer = new StringWriter();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddProvider(new TestLoggerProvider(true, writer));
            });

            var router = new Router();
            router.Use(Middleware.Logger(loggerFactory));
            router.Map("/", new TestService());

            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Communicator = communicator,
                Dispatcher = router
            };

            server.Listen();

            await server.CreateRelativeProxy<IServicePrx>("/").IcePingAsync();

            LogMessage message = JsonSerializer.Deserialize<LogMessage>(writer.ToString());

            Assert.AreEqual(1024, message.EventId);
            Assert.AreEqual("Information", message.LogLevel);
            Assert.AreEqual("IceRpc", message.Category);
            Assert.That(message.Message, Is.Not.Empty);

            Assert.AreEqual(0, message.State.StreamId);
            Assert.AreEqual("ice_ping", message.State.Operation);
            Assert.AreEqual("/", message.State.Path);
            Assert.AreEqual("colocated", message.State.Peer);
            Assert.AreEqual("success", message.State.Status);
            Assert.AreNotEqual(0, message.State.Duration);
            Assert.That(message.Scopes, Is.Empty);

            string expectedMessage = string.Format(
                "[{0}] {1} on {2} from {3} - {4} in {5:0.0000}ms",
                message.State.StreamId,
                message.State.Operation,
                message.State.Path,
                message.State.Peer,
                message.State.Status,
                message.State.Duration);

            Assert.AreEqual(message.State.Message, expectedMessage);
        }

        /// <summary>Test logger middleware with peer ip and failure</summary>
        [Test]
        public async Task BuiltinMiddleware_LoggerPeerAndFailure()
        {
            using var writer = new StringWriter();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddProvider(new TestLoggerProvider(true, writer));
            });

            var router = new Router();
            router.Use(Middleware.Logger(loggerFactory));
            router.Map("/foo", new TestService());

            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Communicator = communicator,
                Dispatcher = router,
                Endpoint = "ice+tcp://127.0.0.1:0"
            };

            server.Listen();

            IDispatchInterceptorTestServicePrx prx = server.CreateProxy<IDispatchInterceptorTestServicePrx>("/foo");

            Assert.ThrowsAsync<ServerException>(async () => await prx.OpAsync());

            LogMessage message = JsonSerializer.Deserialize<LogMessage>(writer.ToString());

            Assert.AreEqual("Op", message.State.Operation);
            Assert.AreEqual("/foo", message.State.Path);
            Assert.AreEqual("127.0.0.1", message.State.Peer);
            Assert.AreEqual("failure=IceRpc.ServerException", message.State.Status);
        }
        public class TestService : IAsyncDispatchInterceptorTestService
        {
            public ValueTask OpAsync(Current current, CancellationToken cancel) => throw new ServerException();
        }
    }
}
