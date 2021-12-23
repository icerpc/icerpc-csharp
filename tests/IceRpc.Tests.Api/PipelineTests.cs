// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(30000)]
    public class PipelineTests
    {
        private const string Message = "hello, world";

        [Test]
        public async Task Pipeline_UseWithAsync()
        {
            int value = 0;
            Func<int> nextValue = () => ++value; // value is captured by reference

            // Make sure interceptors are called in the correct order
            var pipeline = new Pipeline();
            pipeline.Use(CheckValue(nextValue, 1), CheckValue(nextValue, 2), CheckValue(nextValue, 3));

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, Greeter>()
                .BuildServiceProvider();

            var prx = GreeterPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            prx.Proxy.Invoker = pipeline;

            Assert.AreEqual(0, value);
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.AreEqual(3, value);

            // Verify we can't add an extra interceptor now
            Assert.Throws<InvalidOperationException>(() => pipeline.Use(next => next));

            // Add more interceptors with With
            var prx2 = new GreeterPrx(prx.Proxy.Clone());
            prx2.Proxy.Invoker = pipeline.With(CheckValue(nextValue, 4), CheckValue(nextValue, 5));

            value = 0;
            Assert.DoesNotThrowAsync(async () => await prx.IcePingAsync());
            Assert.AreEqual(3, value); // did not change the prx pipeline

            value = 0;
            Assert.DoesNotThrowAsync(async () => await prx2.IcePingAsync());
            Assert.AreEqual(5, value); // 2 more interceptors executed with prx2
        }

        [TestCase(ProtocolCode.Ice1)]
        [TestCase(ProtocolCode.Ice2)]
        public async Task Pipeline_CoalesceInterceptor(ProtocolCode protocol)
        {
            string lastOperation = "";

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, Greeter>()
                .AddTransient<IInvoker>(_ =>
                {
                    var pipeline = new Pipeline();
                    pipeline.Use(next => new InlineInvoker((request, cancel) =>
                    {
                        lastOperation = request.Operation;
                        request.PayloadSink = new CoalescePipeWriterDecorator(request.PayloadSink);
                        return next.InvokeAsync(request, cancel);
                    }));
                    return pipeline;
                })
                .BuildServiceProvider();

            GreeterPrx prx = serviceProvider.GetProxy<GreeterPrx>();
            await prx.IcePingAsync();
            Assert.That(lastOperation, Is.EqualTo("ice_ping"));
            await prx.SayHelloAsync(Message);
            Assert.That(lastOperation, Is.EqualTo("sayHello"));
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
        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel)
            {
                Assert.That(message, Is.EqualTo(Message));
                return default;
            }
        }

        private class CoalescePipeWriterDecorator : PipeWriterDecorator
        {
            public CoalescePipeWriterDecorator(PipeWriter decoratee)
                : base(decoratee)
            {
            }

            public override ValueTask<FlushResult> WriteAsync(
                ReadOnlyMemory<byte> source,
                CancellationToken cancellationToken)
            {
                if (source.Length < 100)
                {
                    // Just store in Decoratee's unflushed memory
                    this.Write(source.Span);
                    return new(new FlushResult());
                }
                else
                {
                    return Decoratee.WriteAsync(source, cancellationToken);
                }
            }
        }
    }
}
