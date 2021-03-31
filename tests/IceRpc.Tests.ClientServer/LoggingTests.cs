// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(10000)]
    public class LoggingTests : ClientServerBaseTest
    {
        /// <summary>Check that connection establishment retries are log with IceRpc category log level is
        /// lower or equal to Debug, there should be 4 log entries one after each retry for a total of 5 attempts.
        /// </summary>
        [Test]
        public async Task Logging_ConnectionRetries()
        {
            using StringWriter writer = new StringWriter();
            using var loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Debug));
            await using var communicator = new Communicator(
                connectionOptions: new()
                {
                    // Speed up windows testing by speeding up the connection failure
                    ConnectTimeout = TimeSpan.FromMilliseconds(200)
                },
                loggerFactory: loggerFactory);

            Assert.CatchAsync<ConnectFailedException>(
                async () => await IServicePrx.Parse("ice+tcp://127.0.0.1/hello", communicator).IcePingAsync());

            List<JsonDocument> logEntries = ParseLogEntries(writer.ToString());
            foreach (JsonDocument entry in logEntries)
            {
                Assert.AreEqual(11, GetEventId(entry));
                Assert.AreEqual("Debug", GetLogLevel(entry));
                Assert.AreEqual("IceRpc", GetCategory(entry));
                Assert.IsTrue(GetMessage(entry).StartsWith(
                    "retrying connection establishment because of retryable exception:",
                    StringComparison.Ordinal));

                JsonElement[] scopes = GetScopes(entry);
                Assert.AreEqual(1, scopes.Length);
                CheckRequestScope(scopes[0]);
            }
            Assert.Greater(logEntries.Count, 0);
        }

        /// <summary>Check that connection establishment retries are not log when IceRpc category log level is
        /// greater than debug.</summary>
        [Test]
        public async Task Logging_Disabled_ConnectionRetries()
        {
            using StringWriter writer = new StringWriter();
            using var loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Information));
            await using var communicator = new Communicator(
                connectionOptions: new()
                {
                    // Speed up windows testing by speeding up the connection failure
                    ConnectTimeout = TimeSpan.FromMilliseconds(200)
                },
                loggerFactory: loggerFactory);

            Assert.CatchAsync<ConnectFailedException>(
                async () => await IServicePrx.Parse("ice+tcp://127.0.0.1/hello", communicator).IcePingAsync());

            Assert.AreEqual("", writer.ToString());
        }

        /// <summary>Check that the protocol and transport logging doesn't emit any ouput for a normal request,
        /// when LogLevel is set to Error</summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task Logging_Disabled_Request(bool colocated)
        {
            using StringWriter writer = new StringWriter();
            using var loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Error));
            await using var communicator = new Communicator(loggerFactory: loggerFactory);

            await using var adapter = CreateServer(communicator, colocated, portNumber: 1);
            adapter.Activate();

            var service = adapter.Add("hello", new TestService(), IServicePrx.Factory);

            Assert.DoesNotThrowAsync(async () => await service.IcePingAsync());

            Assert.AreEqual("", writer.ToString());
        }

        /// <summary>Check that the protocol and transport logging contains the expected output for colocated, and non
        /// colocated invocations.</summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task Logging_Request(bool colocated)
        {
            using StringWriter writer = new StringWriter();
            using var loggerFactory = CreateLoggerFactory(
                writer,
                builder => builder.AddFilter("IceRpc", LogLevel.Information));
            await using var communicator = new Communicator(loggerFactory: loggerFactory);
            await using var adapter = CreateServer(communicator, colocated, portNumber: 2);
            adapter.Activate();

            var service = adapter.Add("hello", new TestService(), IServicePrx.Factory);

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
                switch (eventId)
                {
                    case 7:
                    {
                        Assert.AreEqual("IceRpc", GetCategory(entry));
                        Assert.AreEqual("Information", GetLogLevel(entry));
                        Assert.AreEqual("received ice2 request frame", GetMessage(entry));
                        JsonElement[] scopes = GetScopes(entry);
                        CheckSocketScope(scopes[0], colocated);
                        CheckStreamScope(scopes[1]);
                        CheckRequestScope(scopes[2]);
                        break;
                    }
                    case 18:
                    {
                        Assert.AreEqual("IceRpc", GetCategory(entry));
                        Assert.AreEqual("Information", GetLogLevel(entry));
                        Assert.AreEqual("sending ice2 request frame", GetMessage(entry));
                        JsonElement[] scopes = GetScopes(entry);
                        CheckSocketScope(scopes[0], colocated);
                        CheckRequestScope(scopes[1]);
                        CheckStreamScope(scopes[2]);
                        break;
                    }
                    case 8:
                    {
                        Assert.AreEqual("IceRpc", GetCategory(entry));
                        Assert.AreEqual("Information", GetLogLevel(entry));
                        Assert.AreEqual("received ice2 response frame: result = Success",
                                        GetMessage(entry));
                        JsonElement[] scopes = GetScopes(entry);
                        CheckSocketScope(scopes[0], colocated);
                        CheckRequestScope(scopes[1]);
                        CheckStreamScope(scopes[2]);
                        // The sending of the request always comes before the receiving of the response
                        CollectionAssert.Contains(events, 18);
                        break;
                    }
                    case 19:
                    {
                        Assert.AreEqual("IceRpc", GetCategory(entry));
                        Assert.AreEqual("Information", GetLogLevel(entry));
                        Assert.AreEqual("sending ice2 response frame: result = Success",
                                        GetMessage(entry));
                        JsonElement[] scopes = GetScopes(entry);
                        CheckSocketScope(scopes[0], colocated);
                        CheckStreamScope(scopes[1]);
                        CheckRequestScope(scopes[2]);
                        // The sending of the response always comes before the receiving of the request
                        CollectionAssert.Contains(events, 7);
                        break;
                    }
                    default:
                    {
                        Assert.Fail($"Unexpected event {eventId}");
                        break;
                    }
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

        private static void CheckSocketScope(JsonElement scope, bool colocated)
        {
            if (colocated)
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("socket(colocated", StringComparison.Ordinal));
            }
            else
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("socket(tcp", StringComparison.Ordinal));
            }
        }

        private static void CheckStreamScope(JsonElement scope)
        {
            Assert.AreEqual(0, scope.GetProperty("ID").GetInt64());
            Assert.AreEqual("[client-initiated, bidirectional]", scope.GetProperty("Kind").GetString());
        }

        private static void CheckRequestScope(JsonElement scope)
        {
            Assert.AreEqual("/hello", scope.GetProperty("Path").GetString());
            Assert.AreEqual("ice_ping", scope.GetProperty("Operation").GetString());
            Assert.AreEqual("Ice2", scope.GetProperty("Protocol").GetString());
            Assert.AreEqual(4, scope.GetProperty("PayloadSize").GetInt32());
            Assert.AreEqual("2.0", scope.GetProperty("PayloadEncoding").GetString());
        }

        private Server CreateServer(Communicator communicator, bool colocated, int portNumber) =>
            new Server(communicator,
                colocated switch
                {
                    false => new ServerOptions
                    {
                        Name = "LoggingService",
                        ColocationScope = ColocationScope.None,
                        Endpoint = GetTestEndpoint(port: portNumber)
                    },
                    true => new ServerOptions
                    {
                        Name = "LoggingService",
                        ColocationScope = ColocationScope.Communicator,
                    }
                });

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

        public class TestService : IAsyncLoggingTestService
        {
            public ValueTask OpAsync(Current current, CancellationToken cancel) => default;
        }
    }
}
