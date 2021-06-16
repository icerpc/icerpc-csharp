// Copyright (c) ZeroC, Inc. All rights reserved.

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
        /// lower or equal to Debug, there should be 4 log entries one after each retry for a total of 5 attempts
        // and a last entry for the request exception.
        /// </summary>
        [Test]
        public async Task Logging_ConnectionRetries()
        {
            using var writer = new StringWriter();
            using var loggerFactory = CreateLoggerFactory(
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
            pipeline.Use(Interceptors.Retry(5, loggerFactory: loggerFactory),
                         Interceptors.Binder(pool),
                         Interceptors.Logger(loggerFactory));

            Assert.CatchAsync<ConnectFailedException>(
                async () => await IServicePrx.Parse("ice+tcp://127.0.0.1/hello", pipeline).IcePingAsync());

            List<JsonDocument> logEntries = ParseLogEntries(writer.ToString());
            Assert.AreEqual(10, logEntries.Count);
            var eventIds = new int[] {
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
            using var loggerFactory = CreateLoggerFactory(
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
            pipeline.Use(Interceptors.Retry(5, loggerFactory: loggerFactory),
                         Interceptors.Binder(pool),
                         Interceptors.Logger(loggerFactory));

            Assert.CatchAsync<ConnectFailedException>(
                async () => await IServicePrx.Parse("ice+tcp://127.0.0.1/hello", pipeline).IcePingAsync());

            List<JsonDocument> logEntries = ParseLogEntries(writer.ToString());
            Assert.AreEqual(1, logEntries.Count);
            var entry = logEntries[0];
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
            using var loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Error));
            var pipeline = new Pipeline();
            pipeline.Use(Interceptors.Logger(loggerFactory));

            var router = new Router();
            router.Use(Middleware.Logger(loggerFactory));
            router.Map<IGreeter>(new Greeter());
            await using var server = CreateServer(colocated, portNumber: 1, router);
            server.Listen();

            await using var connection = new Connection
            {
                LoggerFactory = loggerFactory,
                RemoteEndpoint = server.ProxyEndpoint,
            };

            IGreeterPrx service = IGreeterPrx.FromConnection(connection, invoker: pipeline);

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
            using var loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Information));
            var pipeline = new Pipeline();
            pipeline.Use(Interceptors.Logger(loggerFactory));

            var router = new Router();
            router.Map<IGreeter>(new Greeter());
            router.Use(Middleware.Logger(loggerFactory));
            await using Server server = CreateServer(colocated, portNumber: 2, router);
            server.Listen();

            await using var connection = new Connection
            {
                LoggerFactory = loggerFactory,
                RemoteEndpoint = server.ProxyEndpoint,
            };
            IGreeterPrx service = IGreeterPrx.FromConnection(connection, invoker: pipeline);

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
                    //CheckIncomingConnectionScope(scopes[1], colocated);
                    //CheckStreamScope(scopes[2]);
                }
                else if (eventId == (int)ProtocolEvent.SentRequestFrame)
                {
                    Assert.AreEqual("IceRpc", GetCategory(entry));
                    Assert.AreEqual("Information", GetLogLevel(entry));
                    Assert.That(GetMessage(entry).StartsWith("sent request", StringComparison.Ordinal), Is.True);
                    JsonElement[] scopes = GetScopes(entry);
                    //CheckOutgoingConnectionScope(scopes[0], colocated);
                    //CheckStreamScope(scopes[1]);
                }
                else if (eventId == (int)ProtocolEvent.ReceivedResponseFrame)
                {
                    Assert.AreEqual("IceRpc", GetCategory(entry));
                    Assert.AreEqual("Information", GetLogLevel(entry));
                    Assert.That(GetMessage(entry).StartsWith("received response", StringComparison.Ordinal), Is.True);
                    JsonElement[] scopes = GetScopes(entry);
                    //CheckOutgoingConnectionScope(scopes[0], colocated);
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
                    //CheckIncomingConnectionScope(scopes[1], colocated);
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

        private static void CheckOutgoingConnectionScope(JsonElement scope, bool colocated)
        {
            if (colocated)
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("connection(Transport=coloc", StringComparison.Ordinal));
            }
            else
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("connection(Transport=tcp", StringComparison.Ordinal));
            }
        }

        private static void CheckServerScope(JsonElement scope, bool colocated)
        {
            if (colocated)
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("server(Transport=coloc", StringComparison.Ordinal));
            }
            else
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("server(Transport=tcp", StringComparison.Ordinal));
            }
        }

        private static void CheckIncomingConnectionScope(JsonElement scope, bool colocated)
        {
            if (colocated)
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("connection(", StringComparison.Ordinal));
            }
            else
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("connection(", StringComparison.Ordinal));
            }
        }

        private static void CheckStreamScope(JsonElement scope)
        {
            Assert.AreEqual(0, scope.GetProperty("ID").GetInt64());
            Assert.AreEqual("Client", scope.GetProperty("InitiatedBy").GetString());
            Assert.AreEqual("Bidirectional", scope.GetProperty("Kind").GetString());
        }
        private Server CreateServer(bool colocated, int portNumber, IDispatcher dispatcher) =>

            new Server
            {
                HasColocEndpoint = false,
                Dispatcher = dispatcher,
                Endpoint = colocated ? TestHelper.GetUniqueColocEndpoint() : GetTestEndpoint(port: portNumber),
                // TODO use localhost see https://github.com/dotnet/runtime/issues/53447
                HostName = "127.0.0.1"
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

        public class Greeter : IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
