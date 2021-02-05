// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using System;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public class InvocationInterceptorTests : CollocatedTest
    {
        private IInvocationInterceptorTestServicePrx Prx { get; }

        public InvocationInterceptorTests() =>
            Prx = ObjectAdapter.AddWithUUID(new TestService(), IInvocationInterceptorTestServicePrx.Factory);

        /// <summary>Throwing an exception from an invocation interceptor aborts the invocation, and the caller
        /// receives the exception.</summary>
        [Test]
        public void InvocationInterceptor_Throws_ArgumentException()
        {
            var prx = Prx.Clone(invocationInterceptors: ImmutableList.Create<InvocationInterceptor>(
                (target, request, next, cancel) => throw new ArgumentException()));
            Assert.ThrowsAsync<ArgumentException>(async () => await prx.IcePingAsync());
        }

        /// <summary>Ensure that invocation timeout is triggered if the interceptor takes too much time.</summary>
        [Test]
        public void InvocationInterceptor_Timeout_OperationCanceledException()
        {
            var prx = Prx.Clone(
                invocationTimeout: TimeSpan.FromMilliseconds(10),
                invocationInterceptors: ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                        {
                            await Task.Delay(100, default);
                            return await next(target, request, cancel);
                        }));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
        }

        /// <summary>Ensure that invocation interceptors are called in the expected order.</summary>
        [Test]
        public async Task InvocationInterceptor_CallOrder()
        {
            var interceptorCalls = new List<string>();
            var prx = Prx.Clone(
                invocationInterceptors: new InvocationInterceptor[] 
                {
                    async (target, request, next, cancel) =>
                        {
                            interceptorCalls.Add("ProxyInvocationInterceptors -> 0");
                            var result = await next(target, request, cancel);
                            interceptorCalls.Add("ProxyInvocationInterceptors <- 0");
                            return result;
                        },
                    async (target, request, next, cancel) =>
                        {
                            interceptorCalls.Add("ProxyInvocationInterceptors -> 1");
                            var result = await next(target, request, cancel);
                            interceptorCalls.Add("ProxyInvocationInterceptors <- 1");
                            return result;
                        }
                });
            
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
            IncomingResponseFrame? response = null;
            var prx = Prx.Clone(
                invocationInterceptors: new InvocationInterceptor[] 
                {
                    async (target, request, next, cancel) =>
                        {
                            if (response == null)
                            {
                                response = await next(target, request, cancel);
                            }
                            return response;
                        },
                });
            
            int r1 = await prx.OpIntAsync(p1);
            int r2 = await prx.OpIntAsync(p2);
            
            Assert.AreEqual(r1, p1);
            Assert.AreEqual(r2, p1);
            Assert.IsNotNull(response);    
        }

        [Test]
        public async Task InvocationIterceptor_Overwrite_RequestContext()
        {
            var prx = Prx.Clone(
                context: new Dictionary<string, string>()
                {
                    { "foo", "foo" }
                },
                invocationInterceptors: new InvocationInterceptor[]
                {
                    async (target, request, next, cancel) =>
                        {
                            request.WritableContext["foo"] = "bar";
                            return await next(target, request, cancel);
                        },
                });
            var ctx = await prx.OpContextAsync();
            Assert.AreEqual("bar", ctx["foo"]);
            Assert.AreEqual(1, ctx.Count);
        }

        internal class TestService : IAsyncInvocationInterceptorTestService
        {
            public ValueTask<IReadOnlyDictionary<string, string>> OpContextAsync(Current current, CancellationToken cancel) =>
                new(current.Context);
            public ValueTask<int> OpIntAsync(int value, Current current, CancellationToken cancel) => new(value);
        }
    }
}
