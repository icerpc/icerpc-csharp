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
    public class InvocationInterceptorTests
    {
        private Communicator Communicator { get; }
        private ObjectAdapter ObjectAdapter { get; }

        private IInvocationInterceptorTestServicePrx Prx { get; }

        public InvocationInterceptorTests()
        {
            Communicator = new Communicator();
            ObjectAdapter = Communicator.CreateObjectAdapter("TestAdapter-1");
            Prx = ObjectAdapter.AddWithUUID(new TestService(), IInvocationInterceptorTestServicePrx.Factory);
        }

        [OneTimeTearDown]
        public async Task ShutdownAsync() => await Communicator.DestroyAsync();

        /// <summary>If an interceptor throws an exception in its way out the caller can catch this exception.
        /// </summary>
        [Test]
        public async Task ThrowFromInvocationInterceptor()
        {
            await using var communicator = new Communicator
            {
                DefaultInvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    (target, request, next, cancel) => throw new ArgumentException())
            };

            Assert.ThrowsAsync<ArgumentException>(async () => await Prx.IcePingAsync());
        }

        /// <summary>Ensure that invocation timeout is triggered if the interceptor takes too much time.</summary>
        [Test]
        public async Task InvocationInterceptorTimeout()
        {
            await using var communicator = new Communicator
            {
                DefaultInvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    async (target, request, next, cancel) =>
                        {
                            await Task.Delay(100, cancel);
                            return await next(target, request, cancel);
                        })
            };

            var prx = Prx.Clone(invocationTimeout: TimeSpan.FromMilliseconds(10));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
        }

        /// <summary>Ensure that invocation interceptors are called in the expected order.</summary>
        [Test]
        public async Task InvocationInterceptorCallOrder()
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
        public async Task InvocationInterceptorCanBypassRemoteCall(int p1, int p2)
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

        internal class TestService : IAsyncInvocationInterceptorTestService
        {
            public ValueTask<int> OpIntAsync(int value, Current current, CancellationToken cancel) => new(value);
        }
    }
}
