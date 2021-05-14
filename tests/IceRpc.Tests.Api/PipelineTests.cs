// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public class PipelineTests
    {
        [Test]
        public async Task Pipeline_UseWithAsync()
        {
            int value = 0;
            Func<int> nextValue = () => ++value; // value is captured by reference

            // Make sure interceptors are called in the correct order
            var pipeline = new Pipeline();
            pipeline.Use(CheckValue(nextValue, 1), CheckValue(nextValue, 2), CheckValue(nextValue, 3));

            await using var server = new Server
            {
                Dispatcher = new GreeterService(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };

            var prx = IGreeterServicePrx.FromConnection(connection);
            prx.Invoker = pipeline;

            Assert.AreEqual(0, value);
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.AreEqual(3, value);

            // Verify we can't add an extra interceptor now
            Assert.Throws<InvalidOperationException>(() => pipeline.Use(next => next));

            // Add more interceptors with With
            var prx2 = prx.Clone();
            prx2.Invoker = pipeline.With(CheckValue(nextValue, 4), CheckValue(nextValue, 5));

            value = 0;
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.AreEqual(3, value); // did not change the prx pipeline

            value = 0;
            Assert.DoesNotThrowAsync(async () => await prx2.IcePingAsync());
            Assert.AreEqual(5, value); // 2 more interceptors executed with prx2
        }

        // A simple interceptor
        private static Func<IInvoker, IInvoker> CheckValue(Func<int> nextValue, int count) => next =>
            new InlineInvoker((request, cancel) =>
            {
                int value = nextValue();
                Assert.AreEqual(count, value);
                return next.InvokeAsync(request, cancel);
            });

        // TODO: move to shared location?
        public class GreeterService : IGreeterService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
