// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.Internal
{
    // TODO: We should add unit tests for the retry interceptor, logger interceptor and logger middleware and
    // integrate logging testing to these unit tests instead of grouping all the logging tests here.
    // TODO: These tests shouldn't need to be internal tests but since they rely on internal APIs for creating
    // requests they need to be internal for now.
    [Parallelizable(ParallelScope.All)]
    public class LoggingTests
    {
        /// <summary>Check the retry interceptor logging.</summary>
        [Test]
        public async Task Logging_RetryInterceptor()
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<ILoggerFactory>(_ => NullLoggerFactory.Instance)
                .BuildServiceProvider();
            Connection connection = serviceProvider.GetRequiredService<Connection>();
            await connection.ConnectAsync();

            var policy = RetryPolicy.AfterDelay(TimeSpan.FromTicks(1));
            OutgoingRequest request = CreateOutgoingRequest(connection, twoway: true);

            using var loggerFactory = new TestLoggerFactory();
            var pipeline = new Pipeline();
            pipeline.UseRetry(new RetryOptions { MaxAttempts = 3, LoggerFactory = loggerFactory });

            // We don't use a simple UseFeature here because we want to set the retry policy after receiving the
            // response.
            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                IncomingResponse response = await next.InvokeAsync(request, cancel);
                request.Features = request.Features.With(policy);
                return response;
            }));
            await pipeline.InvokeAsync(request);

            Assert.That(loggerFactory.Logger!.Category, Is.EqualTo("IceRpc"));
            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(2));
            TestLoggerEntry entry = loggerFactory.Logger!.Entries[0];
            CheckRequestEntry(
                entry,
                (int)RetryInterceptorEventIds.RetryRequest,
                LogLevel.Information,
                "retrying request because of retryable exception",
                request.Proxy.Path,
                request.Operation,
                connection.NetworkConnectionInformation!.Value.LocalEndpoint,
                connection.NetworkConnectionInformation!.Value.RemoteEndpoint,
                exception: null);

            Assert.That(entry.State["RetryPolicy"], Is.EqualTo(policy));
            Assert.That(entry.State["Attempt"], Is.EqualTo(2));
            Assert.That(entry.State["MaxAttempts"], Is.EqualTo(3));

            entry = loggerFactory.Logger!.Entries[1];
            Assert.That(entry.State["Attempt"], Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Logging_RequestInterceptor(bool twoway)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<ILoggerFactory>(_ => NullLoggerFactory.Instance)
                .BuildServiceProvider();
            Connection connection = serviceProvider.GetRequiredService<Connection>();
            await connection.ConnectAsync();

            OutgoingRequest request = CreateOutgoingRequest(connection, twoway);
            IncomingResponse response = CreateIncomingResponse(request);

            var pipeline = new Pipeline();
            using var loggerFactory = new TestLoggerFactory();
            pipeline.UseLogger(loggerFactory);
            pipeline.Use(next => new InlineInvoker((request, cancel) => Task.FromResult(response)));

            Assert.That(await pipeline.InvokeAsync(request), Is.EqualTo(response));

            Assert.That(loggerFactory.Logger!.Category, Is.EqualTo("IceRpc"));
            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(twoway ? 2 : 1));

            CheckRequestEntry(loggerFactory.Logger!.Entries[0],
                              (int)LoggerInterceptorEventIds.SendingRequest,
                              LogLevel.Information,
                              "sending request",
                              request.Proxy.Path,
                              request.Operation,
                              connection.NetworkConnectionInformation!.Value.LocalEndpoint,
                              connection.NetworkConnectionInformation!.Value.RemoteEndpoint,
                              request.PayloadEncoding);

            if (twoway)
            {
                CheckRequestEntry(loggerFactory.Logger!.Entries[1],
                                  (int)LoggerInterceptorEventIds.ReceivedResponse,
                                  LogLevel.Information,
                                  "received response",
                                  request.Proxy.Path,
                                  request.Operation,
                                  connection.NetworkConnectionInformation!.Value.LocalEndpoint,
                                  connection.NetworkConnectionInformation!.Value.RemoteEndpoint);

                Assert.That(loggerFactory.Logger!.Entries[1].State["ResultType"], Is.EqualTo(response.ResultType));
            }
        }

        [Test]
        public async Task Logging_RequestInterceptor_Exception()
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<ILoggerFactory>(_ => NullLoggerFactory.Instance)
                .BuildServiceProvider();
            Connection connection = serviceProvider.GetRequiredService<Connection>();
            await connection.ConnectAsync();

            OutgoingRequest request = CreateOutgoingRequest(connection, twoway: true);
            var exception = new ArgumentException();

            var pipeline = new Pipeline();
            using var loggerFactory = new TestLoggerFactory();
            pipeline.UseLogger(loggerFactory);
            pipeline.Use(next => new InlineInvoker((request, cancel) => throw exception));

            Assert.CatchAsync<ArgumentException>(async () => await pipeline.InvokeAsync(request));

            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(2));

            CheckRequestEntry(loggerFactory.Logger!.Entries[1],
                              (int)LoggerInterceptorEventIds.InvokeException,
                              LogLevel.Information,
                              "request invocation exception",
                              request.Proxy.Path,
                              request.Operation,
                              connection.NetworkConnectionInformation!.Value.LocalEndpoint,
                              connection.NetworkConnectionInformation!.Value.RemoteEndpoint,
                              exception: exception);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task Logging_RequestMiddleware(bool twoway)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<ILoggerFactory>(_ => NullLoggerFactory.Instance)
                .BuildServiceProvider();
            Connection connection = serviceProvider.GetRequiredService<Connection>();
            await connection.ConnectAsync();

            IncomingRequest request = CreateIncomingRequest(connection, twoway);
            OutgoingResponse response = CreateOutgoingResponse(request);

            var router = new Router();
            using var loggerFactory = new TestLoggerFactory();
            router.UseLogger(loggerFactory);
            router.Use(next => new InlineDispatcher((request, cancel) => new(response)));

            Assert.That(await router.DispatchAsync(request), Is.EqualTo(response));

            Assert.That(loggerFactory.Logger!.Category, Is.EqualTo("IceRpc"));
            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(twoway ? 2 : 1));

            CheckRequestEntry(loggerFactory.Logger!.Entries[0],
                              (int)LoggerMiddlewareEventIds.ReceivedRequest,
                              LogLevel.Information,
                              "received request",
                              request.Path,
                              request.Operation,
                              connection.NetworkConnectionInformation!.Value.RemoteEndpoint,
                              connection.NetworkConnectionInformation!.Value.LocalEndpoint,
                              request.PayloadEncoding);

            if (twoway)
            {
                CheckRequestEntry(loggerFactory.Logger!.Entries[1],
                                  (int)LoggerMiddlewareEventIds.SendingResponse,
                                  LogLevel.Information,
                                  "sending response",
                                  request.Path,
                                  request.Operation,
                                  connection.NetworkConnectionInformation!.Value.RemoteEndpoint,
                                  connection.NetworkConnectionInformation!.Value.LocalEndpoint);

                Assert.That(loggerFactory.Logger!.Entries[1].State["ResultType"], Is.EqualTo(response.ResultType));
            }
        }

        [Test]
        public async Task Logging_RequestMiddleware_Exception()
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<ILoggerFactory>(_ => NullLoggerFactory.Instance)
                .BuildServiceProvider();
            Connection connection = serviceProvider.GetRequiredService<Connection>();
            await connection.ConnectAsync();

            IncomingRequest request = CreateIncomingRequest(connection, twoway: true);
            var exception = new ArgumentException();
            var router = new Router();
            using var loggerFactory = new TestLoggerFactory();
            router.UseLogger(loggerFactory);
            router.Use(next => new InlineDispatcher((request, cancel) => throw exception));

            Assert.CatchAsync<ArgumentException>(async () => await router.DispatchAsync(request));

            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(2));

            CheckRequestEntry(loggerFactory.Logger!.Entries[1],
                              (int)LoggerMiddlewareEventIds.DispatchException,
                              LogLevel.Information,
                              "request dispatch exception",
                              request.Path,
                              request.Operation,
                              connection.NetworkConnectionInformation!.Value.RemoteEndpoint,
                              connection.NetworkConnectionInformation!.Value.LocalEndpoint,
                              exception: exception);
        }

        private static void CheckRequestEntry(
            TestLoggerEntry entry,
            int eventId,
            LogLevel level,
            string messagePrefix,
            string path,
            string operation,
            Endpoint localEndpoint,
            Endpoint remoteEndpoint,
            Encoding? payloadEncoding = null,
            Exception? exception = null)
        {
            Assert.That(entry.EventId.Id, Is.EqualTo(eventId));
            Assert.That(entry.LogLevel, Is.EqualTo(level));
            Assert.That(entry.State["LocalEndpoint"], Is.EqualTo(localEndpoint.ToString()));
            Assert.That(entry.State["RemoteEndpoint"], Is.EqualTo(remoteEndpoint.ToString()));
            Assert.That(entry.State["Path"], Is.EqualTo(path));
            Assert.That(entry.State["Operation"], Is.EqualTo(operation));

            if (payloadEncoding is Encoding encoding)
            {
                Assert.That(entry.State["PayloadEncoding"], Is.EqualTo(encoding));
            }
            Assert.That(entry.Message, Does.StartWith(messagePrefix));
            Assert.That(entry.Exception, Is.EqualTo(exception));
        }

        private static IncomingRequest CreateIncomingRequest(Connection connection, bool twoway) =>
            new(connection.Protocol)
            {
                Connection = connection,
                IsOneway = !twoway,
                Path = "/dummy",
                Operation = "foo",
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(new byte[15])),
                PayloadEncoding = Encoding.Slice20,
                ResponseWriter = new DelayedPipeWriterDecorator()
            };

        private static IncomingResponse CreateIncomingResponse(OutgoingRequest request) => new(request)
        {
            Payload = PipeReader.Create(new ReadOnlySequence<byte>(new byte[10]))
        };

        private static OutgoingRequest CreateOutgoingRequest(Connection connection, bool twoway) =>
            new(new Proxy(connection.Protocol) { Path = "/dummy" })
            {
                Connection = connection,
                IsOneway = !twoway,
                Operation = "foo",
                PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(new byte[15])),
                PayloadEncoding = Encoding.Slice20
            };

        private static OutgoingResponse CreateOutgoingResponse(IncomingRequest request) =>
            new(request)
            {
                PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(new byte[10]))
            };
    }
}
