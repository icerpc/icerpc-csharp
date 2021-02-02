// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using System;
using ZeroC.Ice;

namespace IceRpc.Tests.Interceptors
{
    [Parallelizable(scope: ParallelScope.All)]
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

        /// <summary>If an interceptor throws an exception in its way out the caller can catch this exception.
        /// </summary>
        [Test]
        public async Task OpThrowFromInvocationInterceptor()
        {
            await using var communicator = new Communicator();
            communicator.DefaultInvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                (target, request, next, cancel) =>
                    {
                        throw new ArgumentException();
                    });

            var prx = IObjectPrx.Parse(GetTestProxy("test"), communicator);
            Assert.ThrowsAsync<ArgumentException>(async () => await prx.IcePingAsync());
        }

        /// <summary>Ensure that invocation is canceled if the interceptor takes too much time.</summary>
        [Test]
        public async Task OpInvocationInterceptorTimeout()
        {
            await using var communicator = new Communicator();
            communicator.DefaultInvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                async (target, request, next, cancel) =>
                    {
                        await Task.Delay(100);
                        return await next(target, request, cancel);
                    });

            var prx = IObjectPrx.Parse(GetTestProxy("test"), communicator).Clone(
                invocationTimeout: TimeSpan.FromMilliseconds(10));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IcePingAsync());
        }
    }

    public class TestService : IAsyncTestService
    {
        public ValueTask Op1Async(Current current, CancellationToken cancel) => default;
    }
}
