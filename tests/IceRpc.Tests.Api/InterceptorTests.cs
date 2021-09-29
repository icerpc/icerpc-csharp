// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    [Parallelizable(scope: ParallelScope.All)]
    public sealed class InterceptorTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly InterceptorTestPrx _prx;
        private readonly Server _server;

        public InterceptorTests()
        {
            _server = new Server
            {
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = new InterceptorTest()
            };
            _server.Listen();

            _connection = new Connection { RemoteEndpoint = _server.Endpoint };
            _prx = InterceptorTestPrx.FromConnection(_connection);
        }

        /// <summary>Throwing an exception from an invocation interceptor aborts the invocation, and the caller
        /// receives the exception.</summary>
        [Test]
        public void Interceptor_Throws_ArgumentException()
        {
            var proxy = _prx.Proxy.Clone();
            var prx = new InterceptorTestPrx(proxy);
            var pipeline = new Pipeline();
            proxy.Invoker = pipeline;
            pipeline.Use(next => new InlineInvoker((request, cancel) => throw new ArgumentException("message")));
            Assert.ThrowsAsync<ArgumentException>(async () => await prx.IcePingAsync());
        }

        /// <summary>Ensure that invocation timeout is triggered if the interceptor takes too much time.</summary>
        [Test]
        public void Interceptor_Timeout_OperationCanceledException()
        {
            var proxy = _prx.Proxy.Clone();
            var prx = new InterceptorTestPrx(proxy);
            var pipeline = new Pipeline();
            proxy.Invoker = pipeline;
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
            var proxy = _prx.Proxy.Clone();
            var prx = new InterceptorTestPrx(proxy);
            var pipeline = new Pipeline();
            proxy.Invoker = pipeline;
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

            await prx.IcePingAsync();

            Assert.AreEqual("ProxyInterceptors -> 0", interceptorCalls[0]);
            Assert.AreEqual("ProxyInterceptors -> 1", interceptorCalls[1]);
            Assert.AreEqual("ProxyInterceptors <- 1", interceptorCalls[2]);
            Assert.AreEqual("ProxyInterceptors <- 0", interceptorCalls[3]);
            Assert.AreEqual(4, interceptorCalls.Count);
        }

        /// <summary>Ensure that invocation interceptors can bypass the remote call and directly return a result.
        /// </summary>
        [TestCase(0, 1)]
        public async Task Interceptor_Bypass_RemoteCall(int p1, int p2)
        {
            IncomingResponse? response = null;
            var proxy = _prx.Proxy.Clone();
            var prx = new InterceptorTestPrx(proxy);
            var pipeline = new Pipeline();
            proxy.Invoker = pipeline;
            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                if (response == null)
                {
                    response = await next.InvokeAsync(request, cancel);
                }
                return response;
            }));

            int r1 = await prx.OpIntAsync(p1);
            int r2 = await prx.OpIntAsync(p2);

            Assert.AreEqual(r1, p1);
            Assert.AreEqual(r2, p1);
            Assert.That(response, Is.Not.Null);
        }

        [Test]
        public async Task Interceptor_Overwrite_RequestContext()
        {
            var proxy = _prx.Proxy.Clone();
            var prx = new InterceptorTestPrx(proxy);
            var pipeline = new Pipeline();
            proxy.Invoker = pipeline;

            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                request.Features = request.Features.WithContext(new Dictionary<string, string> { ["foo"] = "bar" });
                return await next.InvokeAsync(request, cancel);
            }));

            SortedDictionary<string, string> ctx = await prx.OpContextAsync(
                new Invocation
                {
                    Context = new Dictionary<string, string> { ["foo"] = "baz" }
                });
            CollectionAssert.AreEqual(ctx, new SortedDictionary<string, string> { ["foo"] = "bar" });
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        internal class InterceptorTest : Service, IInterceptorTest
        {
            public ValueTask<IEnumerable<KeyValuePair<string, string>>> OpContextAsync(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(dispatch.Context);
            public ValueTask<int> OpIntAsync(int value, Dispatch dispatch, CancellationToken cancel) => new(value);
        }
    }
}
