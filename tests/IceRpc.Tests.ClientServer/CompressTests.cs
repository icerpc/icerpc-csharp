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
        private static readonly byte[] _mainPayload =
            Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

        private static readonly byte[] _streamPayload =
            Enumerable.Range(0, 10_000).Select(i => (byte)(i % 128)).ToArray();

        [Test]
        public async Task Compress_RequestPayload(
            [Values(true, false)] bool compressPayload,
            [Values(true, false)] bool withStream)
        {
            bool called = false;

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();

                    int uncompressedLength = withStream ?
                        _mainPayload.Length + _streamPayload.Length : _mainPayload.Length;

                    router.Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            ReadResult readResult = await request.Payload.ReadAtLeastAsync(uncompressedLength, cancel);

                            if (compressPayload)
                            {
                                // Verify we received a compressed payload.
                                Assert.That(readResult.IsCompleted, Is.True);
                                Assert.That(readResult.Buffer.Length, Is.LessThan(uncompressedLength / 2));
                            }
                            else
                            {
                                Assert.That(readResult.Buffer.Length, Is.EqualTo(uncompressedLength));
                            }
                            request.Payload.AdvanceTo(readResult.Buffer.Start); // don't consume/examine anything
                            return await next.DispatchAsync(request, cancel);
                        }));

                    router.UseCompressor();

                    router.Map("/", new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            ReadResult readResult = await request.Payload.ReadAtLeastAsync(uncompressedLength, cancel);

                            byte[] received = readResult.Buffer.ToArray();
                            await request.Payload.CompleteAsync();

                            if (withStream)
                            {
                                Assert.That(received[0.._mainPayload.Length], Is.EqualTo(_mainPayload));
                                Assert.That(received[_mainPayload.Length..], Is.EqualTo(_streamPayload));
                            }
                            else
                            {
                                Assert.That(received, Is.EqualTo(_mainPayload));
                            }
                            called = true;
                            return new OutgoingResponse(request);
                        }));
                    return router;
                })
                .BuildServiceProvider();

            var proxy = Proxy.FromConnection(serviceProvider.GetRequiredService<Connection>(), "/");
            var request = new OutgoingRequest(proxy, "compressRequestPayload")
            {
                PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(_mainPayload))
            };
            if (compressPayload)
            {
                request.Features = new FeatureCollection().With(Features.CompressPayload.Yes);
            }
            if (withStream)
            {
                request.PayloadSourceStream = PipeReader.Create(new ReadOnlySequence<byte>(_streamPayload));
            }

            IInvoker invoker = new Pipeline().UseCompressor();
            Assert.That(called, Is.False);
            IncomingResponse response = await invoker.InvokeAsync(request);
            Assert.That(called, Is.True);
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
        }

        [Test]
        public async Task Compress_ResponsePayload(
            [Values(true, false)] bool compressPayload,
            [Values(true, false)] bool withStream)
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
                                PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(_mainPayload)),
                                PayloadSourceStream = PipeReader.Create(withStream ?
                                    new ReadOnlySequence<byte>(_streamPayload) :
                                    ReadOnlySequence<byte>.Empty)
                            })));
                    return router;
                })
                .BuildServiceProvider();

            var proxy = Proxy.FromConnection(serviceProvider.GetRequiredService<Connection>(), "/");
            var request = new OutgoingRequest(proxy, "compressResponsePayload");

            bool called = false;

            int uncompressedLength = withStream ? _mainPayload.Length + _streamPayload.Length : _mainPayload.Length;

            Pipeline pipeline = new Pipeline().UseCompressor();
            pipeline.Use(next => new InlineInvoker(
                async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);

                    ReadResult readResult = await response.Payload.ReadAtLeastAsync(uncompressedLength, cancel);
                    if (compressPayload)
                    {
                        // Verify we received a compressed payload.
                        Assert.That(readResult.IsCompleted, Is.True);
                        Assert.That(readResult.Buffer.Length, Is.LessThan(uncompressedLength / 2));
                    }
                    else
                    {
                        Assert.That(readResult.Buffer.Length, Is.EqualTo(uncompressedLength));
                    }
                    response.Payload.AdvanceTo(readResult.Buffer.Start); // don't consume/examine anything

                    called = true;
                    return response;
                }));

            Assert.That(called, Is.False);
            IncomingResponse response = await pipeline.InvokeAsync(request);
            Assert.That(called, Is.True);
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));

            ReadResult readResult = await response.Payload.ReadAtLeastAsync(uncompressedLength);
            byte[] received = readResult.Buffer.ToArray();
            await response.Payload.CompleteAsync();

            if (withStream)
            {
                Assert.That(received[0.._mainPayload.Length], Is.EqualTo(_mainPayload));
                Assert.That(received[_mainPayload.Length..], Is.EqualTo(_streamPayload));
            }
            else
            {
                Assert.That(received, Is.EqualTo(_mainPayload));
            }
        }
    }
}
