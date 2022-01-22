// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.ClientServer
{
    // Tests for the Compress interceptor / middleware

    [Parallelizable(ParallelScope.All)]
    public sealed class CompressTests
    {
        private static readonly byte[] _payload =
            Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

        [TestCase(true)]
        [TestCase(false)]
        public async Task Compress_RequestPayload(bool compressPayload)
        {
            bool called = false;

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();

                    router.Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            ReadResult readResult = await request.Payload.ReadAtLeastAsync(_payload.Length, cancel);

                            if (compressPayload)
                            {
                                // Verify we received a compressed payload.
                                Assert.That(readResult.IsCompleted, Is.True);
                                Assert.That(readResult.Buffer.Length, Is.LessThan(_payload.Length / 2));
                            }
                            else
                            {
                                Assert.That(readResult.Buffer.Length, Is.EqualTo(_payload.Length));
                            }
                            request.Payload.AdvanceTo(readResult.Buffer.Start); // don't consume/examine anything
                            return await next.DispatchAsync(request, cancel);
                        }));

                    router.UseCompressor();

                    router.Map("/", new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            ReadResult readResult = await request.Payload.ReadAtLeastAsync(_payload.Length, cancel);

                            byte[] received = readResult.Buffer.ToArray();
                            await request.Payload.CompleteAsync();
                            Assert.That(received, Is.EqualTo(_payload));
                            called = true;
                            return new OutgoingResponse(request);
                        }));
                    return router;
                })
                .BuildServiceProvider();

            var proxy = Proxy.FromConnection(serviceProvider.GetRequiredService<Connection>(), "/");
            var request = new OutgoingRequest(proxy, "compressRequestPayload")
            {
                PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(_payload))
            };
            if (compressPayload)
            {
                request.Features = new FeatureCollection().With(Features.CompressPayload.Yes);
            }

            IInvoker invoker = new Pipeline().UseCompressor();
            Assert.That(called, Is.False);
            IncomingResponse response = await invoker.InvokeAsync(request);
            Assert.That(called, Is.True);
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task Compress_ResponsePayload(bool compressPayload)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    Router router = new Router().UseCompressor();

                    var features = new FeatureCollection();
                    if (compressPayload)
                    {
                        features = features.With(Features.CompressPayload.Yes);
                    }

                    router.Map("/", new InlineDispatcher(
                        (request, cancel) => new(
                            new OutgoingResponse(request)
                            {
                                Features = features,
                                PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(_payload))
                            })));
                    return router;
                })
                .BuildServiceProvider();

            var proxy = Proxy.FromConnection(serviceProvider.GetRequiredService<Connection>(), "/");
            var request = new OutgoingRequest(proxy, "compressResponsePayload");

            bool called = false;
            Pipeline pipeline = new Pipeline().UseCompressor();
            pipeline.Use(next => new InlineInvoker(
                async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);

                    ReadResult readResult = await response.Payload.ReadAtLeastAsync(_payload.Length, cancel);
                    if (compressPayload)
                    {
                        // Verify we received a compressed payload.
                        Assert.That(readResult.IsCompleted, Is.True);
                        Assert.That(readResult.Buffer.Length, Is.LessThan(_payload.Length / 2));
                    }
                    else
                    {
                        Assert.That(readResult.Buffer.Length, Is.EqualTo(_payload.Length));
                    }
                    response.Payload.AdvanceTo(readResult.Buffer.Start); // don't consume/examine anything

                    called = true;
                    return response;
                }));

            IInvoker invoker = pipeline; // TODO: let's fix Pipeline to allow a direct InvokeAsync call

            Assert.That(called, Is.False);
            IncomingResponse response = await invoker.InvokeAsync(request);
            Assert.That(called, Is.True);
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));

            ReadResult readResult = await response.Payload.ReadAtLeastAsync(_payload.Length);
            byte[] received = readResult.Buffer.ToArray();
            await response.Payload.CompleteAsync();
            Assert.That(received, Is.EqualTo(_payload));
        }
    }
}
