// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
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
    [Timeout(5000)]
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

            IncomingResponse response = await pipeline.InvokeAsync(request);
            await response.Payload.CompleteAsync().ConfigureAwait(false);

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
                exception: null);

            Assert.That(entry.State["RetryPolicy"], Is.EqualTo(policy));
            Assert.That(entry.State["Attempt"], Is.EqualTo(2));
            Assert.That(entry.State["MaxAttempts"], Is.EqualTo(3));

            entry = loggerFactory.Logger!.Entries[1];
            Assert.That(entry.State["Attempt"], Is.EqualTo(3));
        }

        private static void CheckRequestEntry(
            TestLoggerEntry entry,
            int eventId,
            LogLevel level,
            string messagePrefix,
            string path,
            string operation,
            Exception? exception = null)
        {
            Assert.That(entry.EventId.Id, Is.EqualTo(eventId));
            Assert.That(entry.LogLevel, Is.EqualTo(level));
            Assert.That(entry.State["Path"], Is.EqualTo(path));
            Assert.That(entry.State["Operation"], Is.EqualTo(operation));
            Assert.That(entry.Message, Does.StartWith(messagePrefix));
            Assert.That(entry.Exception, Is.EqualTo(exception));
        }

        private static OutgoingRequest CreateOutgoingRequest(Connection connection, bool twoway) =>
            new(new Proxy(connection.Endpoint.Protocol) { Path = "/dummy", Connection = connection })
            {
                IsOneway = !twoway,
                Operation = "foo",
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(new byte[15])),
            };
    }
}
