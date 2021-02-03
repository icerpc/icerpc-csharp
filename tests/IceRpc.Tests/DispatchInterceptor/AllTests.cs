// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using System;
using ZeroC.Ice;

namespace IceRpc.Tests.DispatchInterceptors
{
    [Parallelizable]
    public class AllTests : FunctionalTest
    {
        private ITestServicePrx _prx;

        public AllTests() => _prx = null!;

        [OneTimeSetUp]
        public async Task InitializeAsync()
        {
            ObjectAdapter.Add("test", new TestService());
            await ObjectAdapter.ActivateAsync();
            _prx = ITestServicePrx.Parse(GetTestProxy("test"), Communicator);
        }

        [Test]
        public async Task ThrowUserExceptionFromDispatchInterceptor()
        {
            await using var adapter = Communicator.CreateObjectAdapter(
                "TestAdapter-1",
                new ObjectAdapterOptions()
                {
                    Endpoints = GetTestEndpoint(port: 1)
                });

            adapter.DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                (request, current, next, cancel) =>
                {
                    throw new ForbiddenException();
                });
            adapter.Add("test", new TestService());
            await adapter.ActivateAsync();
            var prx = IObjectPrx.Parse(GetTestProxy("test", port: 1), Communicator);

            Assert.ThrowsAsync<ForbiddenException>(() => prx.IcePingAsync());
        }

        [Test]
        public async Task ThrowSystemExceptionFromDispatchInterceptor()
        {
            await using var adapter = Communicator.CreateObjectAdapter(
                "TestAdapter-1",
                new ObjectAdapterOptions()
                {
                    Endpoints = GetTestEndpoint(port: 1)
                });

            adapter.DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                (request, current, next, cancel) =>
                {
                    throw new ArgumentException();
                });
            adapter.Add("test", new TestService());
            await adapter.ActivateAsync();
            var prx = IObjectPrx.Parse(GetTestProxy("test", port: 1), Communicator);

            Assert.ThrowsAsync<UnhandledException>(() => prx.IcePingAsync());
        }

        /// <summary>Ensure that default dispatch interceptors are called in the expected order.</summary>
        [Test]
        public async Task DefaultDispatchInterceptorCallOrder()
        {
            await using var communicator = new Communicator();

            var interceptorCalls = new List<string>();
            communicator.DefaultDispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                async (request, current, next, cancel) =>
                    {
                        interceptorCalls.Add("DefaultDispatchInterceptors -> 0");
                        var result = await next(request, current, cancel);
                        interceptorCalls.Add("DefaultDispatchInterceptors <- 0");
                        return result;
                    },
                async (request, current, next, cancel) =>
                    {
                        interceptorCalls.Add("DefaultDispatchInterceptors -> 1");
                        var result = await next(request, current, cancel);
                        interceptorCalls.Add("DefaultDispatchInterceptors <- 1");
                        return result;
                    });

            await using var adapter = communicator.CreateObjectAdapter(
                "TestAdapter-1",
                new ObjectAdapterOptions()
                {
                    Endpoints = GetTestEndpoint(port: 1)
                });
            adapter.Add("test", new TestService());
            await adapter.ActivateAsync();

            var prx = IObjectPrx.Parse(GetTestProxy("test", port: 1), communicator);

            await prx.IcePingAsync();

            Assert.AreEqual("DefaultDispatchInterceptors -> 0", interceptorCalls[0]);
            Assert.AreEqual("DefaultDispatchInterceptors -> 1", interceptorCalls[1]);
            Assert.AreEqual("DefaultDispatchInterceptors <- 1", interceptorCalls[2]);
            Assert.AreEqual("DefaultDispatchInterceptors <- 0", interceptorCalls[3]);
            Assert.AreEqual(4, interceptorCalls.Count);
        }

        /// <summary>Ensure that object adapter dispatch interceptors are called in the expected order.</summary>
        [Test]
        public async Task ObjectAdapterDispatchInterceptorCallOrder()
        {
            await using var adapter = Communicator.CreateObjectAdapter(
                "TestAdapter-1",
                new ObjectAdapterOptions()
                {
                    Endpoints = GetTestEndpoint(port: 1)
                });
            var interceptorCalls = new List<string>();
            adapter.DispatchInterceptors = ImmutableList.Create<DispatchInterceptor>(
                async (request, current, next, cancel) =>
                {
                    interceptorCalls.Add("AdapterDispatchInterceptors -> 0");
                    var result = await next(request, current, cancel);
                    interceptorCalls.Add("AdapterDispatchInterceptors <- 0");
                    return result;
                },
                async (request, current, next, cancel) =>
                {
                    interceptorCalls.Add("AdapterDispatchInterceptors -> 1");
                    var result = await next(request, current, cancel);
                    interceptorCalls.Add("AdapterDispatchInterceptors <- 1");
                    return result;
                });
            adapter.Add("test", new TestService());
            await adapter.ActivateAsync();

            var prx = IObjectPrx.Parse(GetTestProxy("test", port: 1), Communicator);

            await prx.IcePingAsync();

            Assert.AreEqual("AdapterDispatchInterceptors -> 0", interceptorCalls[0]);
            Assert.AreEqual("AdapterDispatchInterceptors -> 1", interceptorCalls[1]);
            Assert.AreEqual("AdapterDispatchInterceptors <- 1", interceptorCalls[2]);
            Assert.AreEqual("AdapterDispatchInterceptors <- 0", interceptorCalls[3]);
            Assert.AreEqual(4, interceptorCalls.Count);
        }
    }

    public class TestService : IAsyncTestService
    {
        public ValueTask<int> OpIntAsync(int value, Current current, CancellationToken cancel) => new(value);
    }
}
