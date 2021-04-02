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
            Assert.AreEqual(5, logEntries.Count);
            foreach (JsonDocument entry in logEntries)
            {
                Assert.AreEqual(GetEventId(entry) == 128 + 10 ? "Debug" : "Information", GetLogLevel(entry));
                Assert.AreEqual("IceRpc", GetCategory(entry));
                JsonElement[] scopes = GetScopes(entry);
                Assert.IsEmpty(scopes);
                Assert.IsTrue(GetEventId(entry) == 128 + 10 || GetEventId(entry) == 128 + 8);
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
            Assert.AreEqual(1, logEntries.Count);
            var entry = logEntries[0];
            Assert.AreEqual("Information", GetLogLevel(entry));
            Assert.AreEqual("IceRpc", GetCategory(entry));
            JsonElement[] scopes = GetScopes(entry);
            Assert.IsEmpty(scopes);
            Assert.IsTrue(GetEventId(entry) == 128 + 8);
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
            using var writer = new StringWriter();
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
                    case 128 + 7:
                    {
                        Assert.AreEqual("IceRpc", GetCategory(entry));
                        Assert.AreEqual("Information", GetLogLevel(entry));
                        Assert.IsTrue(GetMessage(entry).StartsWith("received request", StringComparison.Ordinal));
                        JsonElement[] scopes = GetScopes(entry);
                        CheckServerScope(scopes[0], colocated);
                        CheckServerSocketScope(scopes[1], colocated);
                        CheckStreamScope(scopes[2]);
                        break;
                    }
                    case 128 + 15:
                    {
                        Assert.AreEqual("IceRpc", GetCategory(entry));
                        Assert.AreEqual("Information", GetLogLevel(entry));
                        Assert.IsTrue(GetMessage(entry).StartsWith("sent request", StringComparison.Ordinal));
                        JsonElement[] scopes = GetScopes(entry);
                        CheckClientSocketScope(scopes[0], colocated);
                        CheckStreamScope(scopes[1]);
                        break;
                    }
                    case 128 + 6:
                    {
                        Assert.AreEqual("IceRpc", GetCategory(entry));
                        Assert.AreEqual("Information", GetLogLevel(entry));
                        Assert.IsTrue(GetMessage(entry).StartsWith("received response", StringComparison.Ordinal));
                        JsonElement[] scopes = GetScopes(entry);
                        CheckClientSocketScope(scopes[0], colocated);
                        CheckStreamScope(scopes[1]);
                        // The sending of the request always comes before the receiving of the response
                        CollectionAssert.Contains(events, 128 + 15);
                        break;
                    }
                    case 128 + 16:
                    {
                        Assert.AreEqual("IceRpc", GetCategory(entry));
                        Assert.AreEqual("Information", GetLogLevel(entry));
                        Assert.IsTrue(GetMessage(entry).StartsWith("sent response", StringComparison.Ordinal));
                        JsonElement[] scopes = GetScopes(entry);
                        CheckServerScope(scopes[0], colocated);
                        CheckServerSocketScope(scopes[1], colocated);
                        CheckStreamScope(scopes[2]);
                        // The sending of the response always comes before the receiving of the request
                        CollectionAssert.Contains(events, 128 + 7);
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

        private static void CheckClientSocketScope(JsonElement scope, bool colocated)
        {
            if (colocated)
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("socket(Transport=colocated", StringComparison.Ordinal));
            }
            else
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("socket(Transport=tcp", StringComparison.Ordinal));
            }
        }

        private static void CheckServerScope(JsonElement scope, bool colocated)
        {
            if (colocated)
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("server(Transport=colocated", StringComparison.Ordinal));
            }
            else
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("server(Transport=tcp", StringComparison.Ordinal));
            }
        }

        private static void CheckServerSocketScope(JsonElement scope, bool colocated)
        {
            if (colocated)
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("socket(", StringComparison.Ordinal));
            }
            else
            {
                Assert.IsTrue(GetMessage(scope).StartsWith("socket(", StringComparison.Ordinal));
            }
        }

        private static void CheckStreamScope(JsonElement scope)
        {
            Assert.AreEqual(0, scope.GetProperty("ID").GetInt64());
            Assert.AreEqual("Client", scope.GetProperty("InitiatedBy").GetString());
            Assert.AreEqual("Bidirectional", scope.GetProperty("Kind").GetString());
        }

        private Server CreateServer(Communicator communicator, bool colocated, int portNumber) =>
            new(communicator,
                colocated switch
                {
                    false => new ServerOptions
                    {
                        Name = "LoggingService",
                        ColocationScope = ColocationScope.None,
                        Endpoints = GetTestEndpoint(port: portNumber)
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
