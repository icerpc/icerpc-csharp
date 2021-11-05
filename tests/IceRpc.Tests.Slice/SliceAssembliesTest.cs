// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Tests.ReferencedAssemblies;
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
            Endpoint endpoint = TestHelper.GetUniqueColocEndpoint(Protocol.FromProtocolCode(ProtocolCode.Ice1));
               await using var server = new Server
            {
                Dispatcher = new AssembliesOperations(),
                Endpoint = endpoint,
            };
            server.Listen();

            await using var connection = new Connection
            {
                RemoteEndpoint = endpoint
            };

            // This should fail because there isn't a factory for ClassB and slicing is not allowed
            var prx = AssembliesOperationsPrx.FromConnection(connection);
            var pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            // Clear the default factories, so that ClassB cannot be found
            SetupResponseIceDecoderFactory(pipeline);
            Assert.ThrowsAsync<InvalidDataException>(async () => await prx.OpAAsync(new ClassB("A", "B")));

            // Repeat but this time setup the SliceAssembliesInterceptor after clearing the factories, this should
            // make the ClassB factory available.
            prx = AssembliesOperationsPrx.FromConnection(connection);
            pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            // Setup the Assemblies interceptors to ensure it correctly setup the factories
            pipeline.UseSliceAssemblies(typeof(ClassB).Assembly);
            // Clear the default factories, so that ClassB cannot be found
            SetupResponseIceDecoderFactory(pipeline);
            await prx.OpAAsync(new ClassB("A", "B"));

            // Set the response decode factories so that ClassB is not available
            static void SetupResponseIceDecoderFactory(Pipeline pipeline)
            {
                var decoderFactory11 = new Ice11DecoderFactory(Ice11Decoder.GetActivator(typeof(ClassA).Assembly));
                var decoderFactory20 = new Ice20DecoderFactory(Ice20Decoder.GetActivator(typeof(ClassA).Assembly));
                pipeline.Use(next => new InlineInvoker(
                async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    if (response.Features.IsReadOnly)
                    {
                        response.Features = new FeatureCollection(response.Features);
                    }
                    response.Features.Set<IIceDecoderFactory<Ice11Decoder>>(decoderFactory11);
                    response.Features.Set<IIceDecoderFactory<Ice20Decoder>>(decoderFactory20);
                    return response;
                }));
            }
        }

        [Test]
        public async Task SliceAssemblies_AssembliesMiddlewareAsync()
        {
            Endpoint endpoint = TestHelper.GetUniqueColocEndpoint(Protocol.FromProtocolCode(ProtocolCode.Ice1));
            {
                var router = new Router();
                SetupRequestIceDecoderFactory(router);
                router.Map<IAssembliesOperations>(new AssembliesOperations());
                await using var server = new Server
                {
                    Dispatcher = router,
                    Endpoint = endpoint,
                };
                server.Listen();

                await using var connection = new Connection
                {
                    RemoteEndpoint = endpoint
                };

                // This should fail because the server doesn't have a factory for ClassB and slicing is not allowed
                var prx = AssembliesOperationsPrx.FromConnection(connection);
                Assert.ThrowsAsync<UnhandledException>(async () => await prx.OpAAsync(new ClassB("A", "B")));
            }

            // Repeat this time setup the assemblies middleware to allow locate ClassB
            {
                var router = new Router();
                SetupRequestIceDecoderFactory(router);
                router.UseSliceAssemblies(typeof(ClassB).Assembly);
                router.Map<IAssembliesOperations>(new AssembliesOperations());
                await using var server = new Server
                {
                    Dispatcher = router,
                    Endpoint = endpoint,
                };
                server.Listen();

                await using var connection = new Connection
                {
                    RemoteEndpoint = endpoint
                };

                // This should fail because the server doesn't have a factory for ClassB and slicing is not allowed
                var prx = AssembliesOperationsPrx.FromConnection(connection);
                await prx.OpAAsync(new ClassB("A", "B"));
            }

            // Set the response decode factories so that ClassB is not available
            static void SetupRequestIceDecoderFactory(Router router)
            {
                var decoderFactory11 = new Ice11DecoderFactory(Ice11Decoder.GetActivator(typeof(ClassA).Assembly));
                var decoderFactory20 = new Ice20DecoderFactory(Ice20Decoder.GetActivator(typeof(ClassA).Assembly));
                router.Use(next => new InlineDispatcher(
                (request, cancel) =>
                {
                    if (request.Features.IsReadOnly)
                    {
                        request.Features = new FeatureCollection(request.Features);
                    }
                    request.Features.Set<IIceDecoderFactory<Ice11Decoder>>(decoderFactory11);
                    request.Features.Set<IIceDecoderFactory<Ice20Decoder>>(decoderFactory20);
                    return next.DispatchAsync(request, cancel);
                }));
            }
        }
    }

    public class AssembliesOperations : Service, IAssembliesOperations
    {
        public ValueTask<ClassA> OpAAsync(ClassA b, Dispatch dispatch, CancellationToken cancel) => new(b);
    }
}
