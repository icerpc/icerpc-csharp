// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class LoggingTests : ClientServerBaseTest
    {
        /// <summary>Check that connection establishment retries are logged with IceRpc category and log level
        /// lower or equal to Debug, there should be 5 log entries one for each attempt.</summary>
        [Test]
        public async Task Logging_ConnectionRetries()
        {
            using var writer = new StringWriter();
            using ILoggerFactory loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Debug));

            await using var pool = new ConnectionPool
            {
                ConnectionOptions = new()
                {
                    // Speed up windows testing by speeding up the connection failure
                    ConnectTimeout = TimeSpan.FromMilliseconds(500)
                },
                LoggerFactory = loggerFactory
            };

            var pipeline = new Pipeline();
            pipeline.UseRetry(new RetryOptions { MaxAttempts = 5, LoggerFactory = loggerFactory })
                    .UseBinder(pool)
                    .UseLogger(loggerFactory);

            Assert.CatchAsync<ConnectFailedException>(
                async () => await ServicePrx.Parse("ice+tcp://127.0.0.1/hello", pipeline).IcePingAsync());

            List<JsonDocument> logEntries = ParseLogEntries(writer.ToString());
            Assert.AreEqual(10, logEntries.Count);
            int[] eventIds = new int[] {
                (int)TransportEvent.ConnectionConnectFailed,
                (int)ProtocolEvent.RetryRequestConnectionException,
                (int)ProtocolEvent.RequestException
            };

            foreach (JsonDocument entry in logEntries)
            {
                string expectedLogLevel = GetEventId(entry) == (int)ProtocolEvent.RequestException ?
                    "Information" : "Debug";

                Assert.AreEqual(expectedLogLevel, GetLogLevel(entry));
                Assert.AreEqual("IceRpc", GetCategory(entry));
                CollectionAssert.Contains(eventIds, GetEventId(entry));
                JsonElement[] scopes = GetScopes(entry);
                if (GetEventId(entry) == (int)TransportEvent.ConnectionConnectFailed)
                {
                    Assert.That(scopes, Is.Not.Empty);
                }
                else
                {
                    Assert.That(scopes, Is.Empty);
                }
            }
        }

        /// <summary>Check that connection establishment retries are not logged when log level is
        /// greater than debug.</summary>
        [Test]
        public async Task Logging_Disabled_ConnectionRetries()
        {
            using var writer = new StringWriter();
            using ILoggerFactory loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Information));

            await using var pool = new ConnectionPool
            {
                ConnectionOptions = new()
                {
                    // Speed up windows testing by speeding up the connection failure
                    ConnectTimeout = TimeSpan.FromMilliseconds(200)
                },
                LoggerFactory = loggerFactory
            };

            var pipeline = new Pipeline();
            pipeline.UseRetry(new RetryOptions { MaxAttempts = 5, LoggerFactory = loggerFactory })
                    .UseBinder(pool)
                    .UseLogger(loggerFactory);

            Assert.CatchAsync<ConnectFailedException>(
                async () => await ServicePrx.Parse("ice+tcp://127.0.0.1/hello", pipeline).IcePingAsync());

            List<JsonDocument> logEntries = ParseLogEntries(writer.ToString());
            Assert.AreEqual(1, logEntries.Count);
            JsonDocument entry = logEntries[0];
            Assert.AreEqual("Information", GetLogLevel(entry));
            Assert.AreEqual("IceRpc", GetCategory(entry));
            JsonElement[] scopes = GetScopes(entry);
            Assert.That(scopes, Is.Empty);
            Assert.That(GetEventId(entry), Is.EqualTo((int)ProtocolEvent.RequestException));
        }

        /// <summary>Check that the protocol and transport logging don't emit any output for a normal request,
        /// when LogLevel is set to Error</summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task Logging_Disabled_Request(bool colocated)
        {
            using var writer = new StringWriter();
            using ILoggerFactory loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Error));
            var pipeline = new Pipeline();
            pipeline.UseLogger(loggerFactory);

            var router = new Router();
            router.UseLogger(loggerFactory);
            router.Map<IGreeter>(new Greeter());
            await using Server server = CreateServer(colocated, portNumber: 1, router);
            server.Listen();

            await using var connection = new Connection
            {
                LoggerFactory = loggerFactory,
                RemoteEndpoint = server.Endpoint,
            };

            var service = GreeterPrx.FromConnection(connection, invoker: pipeline);

            Assert.DoesNotThrowAsync(async () => await service.IcePingAsync());

            Assert.AreEqual("", writer.ToString());
        }

        /// <summary>Check that the protocol and transport logging contains the expected output for colocated, and non
        /// colocated invocations.</summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task Logging_Request(bool colocated)
        {
            using var writer = new StringWriter();
            using ILoggerFactory loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Information));
            var pipeline = new Pipeline();
            pipeline.UseLogger(loggerFactory);

            var router = new Router();
            router.Map<IGreeter>(new Greeter());
            router.UseLogger(loggerFactory);
            await using Server server = CreateServer(colocated, portNumber: 2, router);
            server.Listen();

            await using var connection = new Connection
            {
                LoggerFactory = loggerFactory,
                RemoteEndpoint = server.Endpoint,
            };
            var service = GreeterPrx.FromConnection(connection, invoker: pipeline);

            Assert.DoesNotThrowAsync(async () => await service.IcePingAsync());
            writer.Flush();

            List<JsonDocument> logEntries = ParseLogEntries(writer.ToString());

            var events = new List<int>();
            // The order of sending/received requests and response logs is not deterministic.
            foreach (JsonDocument entry in logEntries)
            {
                int eventId = GetEventId(entry);
                events.Add(eventId);
                CollectionAssert.AllItemsAreUnique(events);
                // TODO The log scopes are not started with interceptor/middleware protocol logging
                if (eventId == (int)ProtocolEvent.ReceivedRequestFrame)
                {
                    Assert.AreEqual("IceRpc", GetCategory(entry));
                    Assert.AreEqual("Information", GetLogLevel(entry));
                    Assert.That(GetMessage(entry).StartsWith("received request", StringComparison.Ordinal), Is.True);
                    JsonElement[] scopes = GetScopes(entry);
                    //CheckServerScope(scopes[0], colocated);
                    //CheckServerConnectionScope(scopes[1], colocated);
                    //CheckStreamScope(scopes[2]);
                }
                else if (eventId == (int)ProtocolEvent.SentRequestFrame)
                {
                    Assert.AreEqual("IceRpc", GetCategory(entry));
                    Assert.AreEqual("Information", GetLogLevel(entry));
                    Assert.That(GetMessage(entry).StartsWith("sent request", StringComparison.Ordinal), Is.True);
                    JsonElement[] scopes = GetScopes(entry);
                    //CheckClientConnectionScope(scopes[0], colocated);
                    //CheckStreamScope(scopes[1]);
                }
                else if (eventId == (int)ProtocolEvent.ReceivedResponseFrame)
                {
                    Assert.AreEqual("IceRpc", GetCategory(entry));
                    Assert.AreEqual("Information", GetLogLevel(entry));
                    Assert.That(GetMessage(entry).StartsWith("received response", StringComparison.Ordinal), Is.True);
                    JsonElement[] scopes = GetScopes(entry);
                    //CheckClientConnectionScope(scopes[0], colocated);
                    //CheckStreamScope(scopes[1]);
                    // The sending of the request always comes before the receiving of the response
                    CollectionAssert.Contains(events, (int)ProtocolEvent.SentRequestFrame);
                }
                else if (eventId == (int)ProtocolEvent.SentResponseFrame)
                {
                    Assert.AreEqual("IceRpc", GetCategory(entry));
                    Assert.AreEqual("Information", GetLogLevel(entry));
                    Assert.That(GetMessage(entry).StartsWith("sent response", StringComparison.Ordinal), Is.True);
                    JsonElement[] scopes = GetScopes(entry);
                    //CheckServerScope(scopes[0], colocated);
                    //CheckServerConnectionScope(scopes[1], colocated);
                    //CheckStreamScope(scopes[2]);
                    // The sending of the response always comes before the receiving of the request
                    CollectionAssert.Contains(events, (int)ProtocolEvent.SentResponseFrame);
                }
                else
                {
                    Assert.Fail($"Unexpected event {eventId}");
                }
            }
        }

        private static ILoggerFactory CreateLoggerFactory(TextWriter writer, Action<ILoggingBuilder> loggerBuilder) =>
            LoggerFactory.Create(builder =>
                {
                    builder.ClearProviders();
                    loggerBuilder(builder);
                    builder.AddProvider(new TestLoggerProvider(indented: false,
                                                               writer: TextWriter.Synchronized(writer)));
                });

        private Server CreateServer(bool colocated, int portNumber, IDispatcher dispatcher) => new()
        {
            Dispatcher = dispatcher,
            Endpoint = colocated ? TestHelper.GetUniqueColocEndpoint() : GetTestEndpoint(port: portNumber),
        };

        private static string GetCategory(JsonDocument document) =>
            GetPropertyAsString(document.RootElement, "Category");

        private static string GetLogLevel(JsonDocument document) =>
            GetPropertyAsString(document.RootElement, "LogLevel");

        private static string GetMessage(JsonDocument document) =>
            GetMessage(document.RootElement);

        private static string GetMessage(JsonElement element) =>
            GetPropertyAsString(element, "Message");

        private static int GetEventId(JsonDocument document) =>
            document.RootElement.GetProperty("EventId").GetInt32();

        private static string GetPropertyAsString(JsonElement element, string name) =>
            element.GetProperty(name).GetString()!;

        private static JsonElement[] GetScopes(JsonDocument document) =>
            document.RootElement.GetProperty("Scopes").EnumerateArray().ToArray();

        private static List<JsonDocument> ParseLogEntries(string data) =>
            data.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Select(
                line => JsonDocument.Parse(line)).ToList();

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
