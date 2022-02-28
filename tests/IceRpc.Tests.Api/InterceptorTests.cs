// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.Api
{
    [Timeout(5000)]
    [Parallelizable(scope: ParallelScope.All)]
    public sealed class InterceptorTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly InterceptorTestPrx _prx;

        public InterceptorTests()
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, InterceptorTest>()
                .BuildServiceProvider();
            _prx = InterceptorTestPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        /// <summary>Throwing an exception from an invocation interceptor aborts the invocation, and the caller
        /// receives the exception.</summary>
        [Test]
        public void Interceptor_Throws_ArgumentException()
        {
            var pipeline = new Pipeline();
            var prx = new ServicePrx(_prx.Proxy with { Invoker = pipeline });

            pipeline.Use(next => new InlineInvoker((request, cancel) => throw new ArgumentException("message")));
            Assert.ThrowsAsync<ArgumentException>(async () => await prx.IcePingAsync());
        }

        /// <summary>Ensure that invocation timeout is triggered if the interceptor takes too much time.</summary>
        [Test]
        public void Interceptor_Timeout_OperationCanceledException()
        {
            var pipeline = new Pipeline();
            var prx = new ServicePrx(_prx.Proxy with { Invoker = pipeline });

            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                await Task.Delay(100, default);
                return await next.InvokeAsync(request, cancel);
            }));

            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync(
                new Invocation { Timeout = TimeSpan.FromMilliseconds(10) }));
        }

        /// <summary>Ensure that invocation interceptors are called in the expected order.</summary>
        [Test]
        public async Task Interceptor_CallOrder()
        {
            var interceptorCalls = new List<string>();
            var pipeline = new Pipeline();
            var prx = new InterceptorTestPrx(_prx.Proxy with { Invoker = pipeline });

            pipeline.Use(
                next => new InlineInvoker(async (request, cancel) =>
                {
                    interceptorCalls.Add("ProxyInterceptors -> 0");
                    IncomingResponse result = await next.InvokeAsync(request, cancel);
                    interceptorCalls.Add("ProxyInterceptors <- 0");
                    return result;
                }),
                next => new InlineInvoker(async (request, cancel) =>
                {
                    interceptorCalls.Add("ProxyInterceptors -> 1");
                    IncomingResponse result = await next.InvokeAsync(request, cancel);
                    interceptorCalls.Add("ProxyInterceptors <- 1");
                    return result;
                }));

            await new ServicePrx(prx.Proxy).IcePingAsync();

            Assert.That(interceptorCalls[0], Is.EqualTo("ProxyInterceptors -> 0"));
            Assert.That(interceptorCalls[1], Is.EqualTo("ProxyInterceptors -> 1"));
            Assert.That(interceptorCalls[2], Is.EqualTo("ProxyInterceptors <- 1"));
            Assert.That(interceptorCalls[3], Is.EqualTo("ProxyInterceptors <- 0"));
            Assert.That(interceptorCalls.Count, Is.EqualTo(4));
        }

        /// <summary>Ensure that invocation interceptors can bypass the remote call and directly return a result.
        /// </summary>
        [TestCase(0, 1)]
        public async Task Interceptor_Bypass_RemoteCall(int p1, int p2)
        {
            IncomingResponse? response = null;
            ReadOnlySequence<byte> savedPayload = default;

            var pipeline = new Pipeline();
            var prx = new InterceptorTestPrx(_prx.Proxy with { Invoker = pipeline });

            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                if (response == null)
                {
                    response = await next.InvokeAsync(request, cancel);

                    ReadResult readResult = await response.Payload.ReadAllAsync(cancel);
                    savedPayload = new ReadOnlySequence<byte>(readResult.Buffer.ToArray());
                    response.Payload.AdvanceTo(readResult.Buffer.End);
                }

                response.Payload = PipeReader.Create(savedPayload); // restore saved payload
                return response;
            }));

            int r1 = await prx.OpIntAsync(p1);
            int r2 = await prx.OpIntAsync(p2);

            Assert.That(p1, Is.EqualTo(r1));
            Assert.That(p1, Is.EqualTo(r2));
            Assert.That(response, Is.Not.Null);
        }

        [Test]
        public async Task Interceptor_Overwrite_RequestContext()
        {
            var pipeline = new Pipeline();
            var prx = new InterceptorTestPrx(_prx.Proxy with { Invoker = pipeline });

            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                request.Features = request.Features.WithContext(new Dictionary<string, string> { ["foo"] = "bar" });
                return await next.InvokeAsync(request, cancel);
            }));

            Dictionary<string, string> ctx = await prx.OpContextAsync(
                new Invocation
                {
                    Features = new FeatureCollection().WithContext(new Dictionary<string, string> { ["foo"] = "baz" })
                });
            Assert.That(new SortedDictionary<string, string> { ["foo"] = "bar" }, Is.EqualTo(ctx));
        }

        public class InterceptorTest : Service, IInterceptorTest
        {
            public ValueTask<IEnumerable<KeyValuePair<string, string>>> OpContextAsync(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(dispatch.Features.GetContext());

            public ValueTask<int> OpIntAsync(int value, Dispatch dispatch, CancellationToken cancel) => new(value);
        }
    }
}
