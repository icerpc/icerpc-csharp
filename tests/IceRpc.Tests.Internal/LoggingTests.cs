// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Configure;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

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
        public void Logging_RetryInterceptor()
        {
            using var loggerFactory = new TestLoggerFactory();

            var policy = RetryPolicy.AfterDelay(TimeSpan.FromTicks(1));
            var exception = new ConnectFailedException("test message");
            OutgoingRequest request = CreateOutgoingRequest(twoway: true);

            var pipeline = new Pipeline();
            pipeline.UseRetry(new RetryOptions { MaxAttempts = 3, LoggerFactory = loggerFactory });
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
                {
                    request.RetryPolicy = policy;
                    throw exception;
                }));

            Assert.CatchAsync<ConnectFailedException>(async() => await pipeline.InvokeAsync(request, default));

            Assert.That(loggerFactory.Logger!.Category, Is.EqualTo("IceRpc"));
            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(2));
            TestLoggerEntry entry = loggerFactory.Logger!.Entries[0];
            CheckRequestEntry(
                entry,
                (int)RetryInterceptorEventIds.RetryRequest,
                LogLevel.Debug, // TODO: Should use Information instead?
                "retrying request because of retryable exception",
                request.Path,
                request.Operation,
                exception: exception);

            Assert.That(entry.State["RetryPolicy"], Is.EqualTo(policy));
            Assert.That(entry.State["Attempt"], Is.EqualTo(2));
            Assert.That(entry.State["MaxAttempts"], Is.EqualTo(3));

            entry = loggerFactory.Logger!.Entries[1];
            Assert.That(entry.State["Attempt"], Is.EqualTo(3));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Logging_RequestInterceptor(bool twoway)
        {
            using var loggerFactory = new TestLoggerFactory();

            OutgoingRequest request = CreateOutgoingRequest(twoway);
            IncomingResponse response = CreateIncomingResponse(request);

            var pipeline = new Pipeline();
            pipeline.UseLogger(loggerFactory);
            pipeline.Use(next => new InlineInvoker((request, cancel) => Task.FromResult(response)));

            Assert.That(pipeline.InvokeAsync(request, default).Result, Is.EqualTo(response));

            Assert.That(loggerFactory.Logger!.Category, Is.EqualTo("IceRpc"));
            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(twoway ? 2 : 1));

            CheckRequestEntry(loggerFactory.Logger!.Entries[0],
                              (int)LoggerInterceptorEventIds.SendingRequest,
                              LogLevel.Information,
                              "sending request",
                              request.Path,
                              request.Operation,
                              request.PayloadSize,
                              request.PayloadEncoding);

            if (twoway)
            {
                CheckRequestEntry(loggerFactory.Logger!.Entries[1],
                                (int)LoggerInterceptorEventIds.ReceivedResponse,
                                LogLevel.Information,
                                "received response",
                                request.Path,
                                request.Operation,
                                response.PayloadSize,
                                response.PayloadEncoding);

                Assert.That(loggerFactory.Logger!.Entries[1].State["ResultType"], Is.EqualTo(response.ResultType));
            }
        }

        [Test]
        public void Logging_RequestInterceptor_Exception()
        {
            using var loggerFactory = new TestLoggerFactory();

            OutgoingRequest request = CreateOutgoingRequest(twoway: true);
            var exception = new ArgumentException();

            var pipeline = new Pipeline();
            pipeline.UseLogger(loggerFactory);
            pipeline.Use(next => new InlineInvoker((request, cancel) => throw exception));

            Assert.CatchAsync<ArgumentException>(async () => await pipeline.InvokeAsync(request, default));

            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(2));

            CheckRequestEntry(loggerFactory.Logger!.Entries[1],
                              (int)LoggerInterceptorEventIds.InvokeException,
                              LogLevel.Information,
                              "request invocation exception",
                              request.Path,
                              request.Operation,
                              exception: exception);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Logging_RequestMiddleware(bool twoway)
        {
            using var loggerFactory = new TestLoggerFactory();

            IncomingRequest request = CreateIncomingRequest(twoway);
            OutgoingResponse response = CreateOutgoingResponse(request);

            var router = new Router();
            router.UseLogger(loggerFactory);
            router.Use(next => new InlineDispatcher((request, cancel) => new(response)));

            Assert.That(((IDispatcher)router).DispatchAsync(request, default).AsTask().Result, Is.EqualTo(response));

            Assert.That(loggerFactory.Logger!.Category, Is.EqualTo("IceRpc"));
            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(twoway ? 2 : 1));

            CheckRequestEntry(loggerFactory.Logger!.Entries[0],
                              (int)LoggerMiddlewareEventIds.ReceivedRequest,
                              LogLevel.Information,
                              "received request",
                              request.Path,
                              request.Operation,
                              request.PayloadSize,
                              request.PayloadEncoding);

            if (twoway)
            {
                CheckRequestEntry(loggerFactory.Logger!.Entries[1],
                                (int)LoggerMiddlewareEventIds.SendingResponse,
                                LogLevel.Information,
                                "sending response",
                                request.Path,
                                request.Operation,
                                response.PayloadSize,
                                response.PayloadEncoding);

                Assert.That(loggerFactory.Logger!.Entries[1].State["ResultType"], Is.EqualTo(response.ResultType));
            }
        }

        [Test]
        public void Logging_RequestMiddleware_Exception()
        {
            using var loggerFactory = new TestLoggerFactory();

            IncomingRequest request = CreateIncomingRequest(twoway: true);
            var exception = new ArgumentException();

            var router = new Router();
            router.UseLogger(loggerFactory);
            router.Use(next => new InlineDispatcher((request, cancel) => throw exception));

            Assert.CatchAsync<ArgumentException>(async () => await ((IDispatcher)router).DispatchAsync(request, default));

            Assert.That(loggerFactory.Logger!.Entries.Count, Is.EqualTo(2));

            CheckRequestEntry(loggerFactory.Logger!.Entries[1],
                              (int)LoggerMiddlewareEventIds.DispatchException,
                              LogLevel.Information,
                              "request dispatch exception",
                              request.Path,
                              request.Operation,
                              exception: exception);
        }

        private static void CheckRequestEntry(
            TestLoggerEntry entry,
            int eventId,
            LogLevel level,
            string messagePrefix,
            string path,
            string operation,
            int? payloadSize = null,
            Encoding? payloadEncoding = null,
            Exception? exception = null)
        {
            Assert.That(entry.EventId.Id, Is.EqualTo(eventId));
            Assert.That(entry.LogLevel, Is.EqualTo(level));
            Assert.That(entry.State["LocalEndpoint"], Is.EqualTo("ice+tcp://local:4500"));
            Assert.That(entry.State["RemoteEndpoint"], Is.EqualTo("ice+tcp://remote:4500"));
            Assert.That(entry.State["Path"], Is.EqualTo(path));
            Assert.That(entry.State["Operation"], Is.EqualTo(operation));
            if (payloadSize is int size)
            {
                Assert.That(entry.State["PayloadSize"], Is.EqualTo(size));
            }
            if (payloadEncoding is Encoding encoding)
            {
                Assert.That(entry.State["PayloadEncoding"], Is.EqualTo(encoding));
            }
            Assert.That(entry.Message, Does.StartWith(messagePrefix));
            Assert.That(entry.Exception, Is.EqualTo(exception));
        }

        private static IncomingRequest CreateIncomingRequest(bool twoway)
        {
            var request = CreateOutgoingRequest(twoway).ToIncoming();
            request.Connection = ConnectionStub.Create("ice+tcp://local:4500", "ice+tcp://remote:4500", true);
            return request;
        }

        private static IncomingResponse CreateIncomingResponse(OutgoingRequest outgoingRequest) =>
            CreateOutgoingResponse(outgoingRequest.ToIncoming()).ToIncoming();

        private static OutgoingRequest CreateOutgoingRequest(bool twoway)
        {
            var proxy = Proxy.FromPath("/dummy", Protocol.Ice2);
            var request = new OutgoingRequest(
                proxy,
                "foo",
                Payload.FromEmptyArgs(proxy),
                null,
                DateTime.MaxValue,
                oneway: !twoway);
            request.Connection = ConnectionStub.Create("ice+tcp://local:4500", "ice+tcp://remote:4500", false);
            return request;
        }

        private static OutgoingResponse CreateOutgoingResponse(IncomingRequest incomingRequest) =>
            new(incomingRequest, Payload.FromVoidReturnValue(incomingRequest));
    }
}
