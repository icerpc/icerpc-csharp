// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
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

        [TestCase("ice")]
        [TestCase("icerpc")]
        [Ignore("CoalescePipeWriterDecorator WriteAsync is not a legitimate implementation and this doesn't work with icerpc")]
        public async Task Pipeline_CoalesceInterceptor(string protocol)
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

            GreeterPrx greeter = serviceProvider.GetProxy<GreeterPrx>();
            var service = new ServicePrx(greeter.Proxy);
            await service.IcePingAsync();
            Assert.That(lastOperation, Is.EqualTo("ice_ping"));
            await greeter.SayHelloAsync(Message);
            Assert.That(lastOperation, Is.EqualTo("sayHello"));
        }

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
