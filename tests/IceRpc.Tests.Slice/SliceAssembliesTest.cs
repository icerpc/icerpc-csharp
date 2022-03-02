// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Tests.ReferencedAssemblies;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class SliceAssembliesTests
    {
        [Test]
        public async Task SliceAssemblies_AssembliesInterceptorAsync()
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, AssembliesOperations>()
                .BuildServiceProvider();

            var prx = AssembliesOperationsPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

            // This should fail the client has no factory for ClassB and compact format prevents slicing
            var pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            // Setup response activator excluding ClassB assembly
            pipeline.UseFeature(
                new SliceDecodePayloadOptions { Activator = SliceDecoder.GetActivator(typeof(ClassA).Assembly) });
            Assert.ThrowsAsync<InvalidDataException>(async () => await prx.OpAAsync(new ClassB("A", "B")));

            // Repeat but this time include ClassB assembly
            pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            pipeline.UseFeature(
                new SliceDecodePayloadOptions { Activator = SliceDecoder.GetActivator(typeof(ClassB).Assembly) });
            await prx.OpAAsync(new ClassB("A", "B"));
        }

        [Test]
        public async Task SliceAssemblies_AssembliesMiddlewareAsync()
        {
            {
                await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                    .UseProtocol("ice")
                    .AddTransient<IDispatcher>(_ =>
                    {
                        var router = new Router();
                        router.UseFeature(
                            new SliceDecodePayloadOptions
                            {
                                Activator = SliceDecoder.GetActivator(typeof(ClassA).Assembly)
                            });
                        router.Map<IAssembliesOperations>(new AssembliesOperations());
                        return router;
                    })
                    .BuildServiceProvider();

                // This should fail the server has no factory for ClassB and compact format prevents slicing
                var prx = AssembliesOperationsPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
                var dispatchException = Assert.ThrowsAsync<DispatchException>(() => prx.OpAAsync(new ClassB("A", "B")));
                Assert.That(dispatchException!.ErrorCode, Is.EqualTo(DispatchErrorCode.InvalidData));
            }

            // Repeat but this time include ClassB assembly
            {
                await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                    .UseProtocol("ice")
                    .AddTransient<IDispatcher>(_ =>
                    {
                        var router = new Router();
                        router.UseFeature(
                            new SliceDecodePayloadOptions
                            {
                                Activator = SliceDecoder.GetActivator(typeof(ClassB).Assembly)
                            });
                        router.Map<IAssembliesOperations>(new AssembliesOperations());
                        return router;
                    })
                    .BuildServiceProvider();

                var prx = AssembliesOperationsPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
                await prx.OpAAsync(new ClassB("A", "B"));
            }
        }
    }

    public class AssembliesOperations : Service, IAssembliesOperations
    {
        public ValueTask<ClassA> OpAAsync(ClassA b, Dispatch dispatch, CancellationToken cancel) => new(b);
    }
}
