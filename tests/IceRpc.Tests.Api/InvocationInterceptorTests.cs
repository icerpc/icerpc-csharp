// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public class InvocationInterceptorTests : ColocTest
    {
        private IInvocationInterceptorTestServicePrx Prx { get; }

        public InvocationInterceptorTests()
        {
            Server.Dispatcher = new TestService();
            Server.Listen();
            Prx = Server.CreateProxy<IInvocationInterceptorTestServicePrx>("/");
        }

        /// <summary>Throwing an exception from an invocation interceptor aborts the invocation, and the caller
        /// receives the exception.</summary>
        [Test]
        public async Task InvocationInterceptor_Throws_ArgumentExceptionAsync()
        {
            var prx = Prx.Clone();
            await using var pool = new Communicator();
            prx.Invoker = pool;
            pool.Use(next => new InlineInvoker((request, cancel) => throw new ArgumentException("message")));
            Assert.ThrowsAsync<ArgumentException>(async () => await prx.IcePingAsync());
        }

        /// <summary>Ensure that invocation timeout is triggered if the interceptor takes too much time.</summary>
        [Test]
        public async Task InvocationInterceptor_Timeout_OperationCanceledExceptionAsync()
        {
            var prx = Prx.Clone();
            await using var pool = new Communicator();
            prx.Invoker = pool;
            prx.InvocationTimeout = TimeSpan.FromMilliseconds(10);
            pool.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                await Task.Delay(100, default);
                return await next.InvokeAsync(request, cancel);
            }));

            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
        }

        /// <summary>Ensure that invocation interceptors are called in the expected order.</summary>
        [Test]
        public async Task InvocationInterceptor_CallOrder()
        {
            var interceptorCalls = new List<string>();
            var prx = Prx.Clone();
            await using var pool = new Communicator();
            prx.Invoker = pool;
            pool.Use(
                next => new InlineInvoker(async (request, cancel) =>
                {
                    interceptorCalls.Add("ProxyInvocationInterceptors -> 0");
                    var result = await next.InvokeAsync(request, cancel);
                    interceptorCalls.Add("ProxyInvocationInterceptors <- 0");
                    return result;
                }),
                next => new InlineInvoker(async (request, cancel) =>
                {
                    interceptorCalls.Add("ProxyInvocationInterceptors -> 1");
                    var result = await next.InvokeAsync(request, cancel);
                    interceptorCalls.Add("ProxyInvocationInterceptors <- 1");
                    return result;
                }));

            await prx.IcePingAsync();

            Assert.AreEqual("ProxyInvocationInterceptors -> 0", interceptorCalls[0]);
            Assert.AreEqual("ProxyInvocationInterceptors -> 1", interceptorCalls[1]);
            Assert.AreEqual("ProxyInvocationInterceptors <- 1", interceptorCalls[2]);
            Assert.AreEqual("ProxyInvocationInterceptors <- 0", interceptorCalls[3]);
            Assert.AreEqual(4, interceptorCalls.Count);
        }

        /// <summary>Ensure that invocation interceptors can bypass the remote call and directly return a result.
        /// </summary>
        [TestCase(0, 1)]
        public async Task InvocationInterceptor_Bypass_RemoteCall(int p1, int p2)
        {
            IncomingResponse? response = null;
            var prx = Prx.Clone();
            await using var pool = new Communicator();
            prx.Invoker = pool;
            pool.Use(next => new InlineInvoker(async (request, cancel) =>
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
            Assert.IsNotNull(response);
        }

        [Test]
        public async Task InvocationInterceptor_Overwrite_RequestContext()
        {
            var prx = Prx.Clone();
            await using var pool = new Communicator();
            prx.Invoker = pool;
            prx.Context = new Dictionary<string, string>()
                {
                    { "foo", "foo" }
                };

            pool.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                request.WritableContext["foo"] = "bar";
                return await next.InvokeAsync(request, cancel);
            }));

            var ctx = await prx.OpContextAsync();
            Assert.AreEqual("bar", ctx["foo"]);
            Assert.AreEqual(1, ctx.Count);
        }

        internal class TestService : IInvocationInterceptorTestService
        {
            public ValueTask<IReadOnlyDictionary<string, string>> OpContextAsync(Dispatch dispatch, CancellationToken cancel) =>
                new(dispatch.Context);
            public ValueTask<int> OpIntAsync(int value, Dispatch dispatch, CancellationToken cancel) => new(value);
        }
    }
}
