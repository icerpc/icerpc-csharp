// Copyright (c) ZeroC, Inc. All rights reserved.

// #define LOG_PIPE_WRITER

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;

#if LOG_PIPE_WRITER
using Microsoft.Extensions.Logging;
#endif

using NUnit.Framework;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Tests.ClientServer
{
    // Tests for the Compress interceptor and middleware

    [Parallelizable(ParallelScope.All)]
    [Timeout(10000)]
    public sealed class CompressTests
    {
        // The minimum compression factor we expect for the payloads
        private const float CompressionFactor = 0.5f;

        private static readonly byte[] _mainPayload =
            Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();

        private static readonly byte[] _streamPayload =
            Enumerable.Range(0, 10_000).Select(i => (byte)(i % 128)).ToArray();

        [Test]
        public async Task Compress_RequestPayload(
            [Values] bool compressPayload,
            [Values] bool withStream)
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
                                Assert.That(
                                    readResult.Buffer,
                                    Has.Length.LessThan(uncompressedLength * CompressionFactor));
                            }
                            else
                            {
                                Assert.That(readResult.Buffer, Has.Length.EqualTo(uncompressedLength));
                            }
                            // Don't consume anything.
                            request.Payload.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
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
            var request = new OutgoingRequest(proxy)
            {
                Operation = "compressRequestPayload",
                PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(_mainPayload))
            };
            if (compressPayload)
            {
                request.Features = request.Features.With(Features.CompressPayload.Yes);
            }
            if (withStream)
            {
                request.PayloadSourceStream = PipeReader.Create(new ReadOnlySequence<byte>(_streamPayload));
            }

            var pipeline = new Pipeline();

#if LOG_PIPE_WRITER

            using var loggerFactory = LoggerFactory.Create(
                builder => _ = builder.AddConsole().SetMinimumLevel(LogLevel.Information));

            // Add timeout interceptor to get a CanBeCanceled cancellation token.
            pipeline.UseTimeout(TimeSpan.FromSeconds(1));

            pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    request.PayloadSink = new LogPipeWriterDecorator(
                        request.PayloadSink,
                        loggerFactory.CreateLogger("InnerPipeWriter"));
                    return next.InvokeAsync(request, cancel);
                }));
#endif

            pipeline.UseCompressor(new CompressOptions { CompressionLevel = CompressionLevel.Fastest });

#if LOG_PIPE_WRITER
            pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    request.PayloadSink = new LogPipeWriterDecorator(
                        request.PayloadSink,
                        loggerFactory.CreateLogger("OuterPipeWriter"));
                    return next.InvokeAsync(request, cancel);
                }));
#endif

            Assert.That(called, Is.False);
            IncomingResponse response = await pipeline.InvokeAsync(request);
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
            Assert.That(called, Is.True);
        }

        [Test]
        public async Task Compress_ResponsePayload(
            [Values] bool compressPayload,
            [Values] bool withStream)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    Router router = new Router().UseCompressor();

                    if (compressPayload)
                    {
                        router.UseFeature(Features.CompressPayload.Yes);
                    }

                    router.Map("/", new InlineDispatcher(
                        (request, cancel) => new(
                            new OutgoingResponse(request)
                            {
                                PayloadSource = PipeReader.Create(new ReadOnlySequence<byte>(_mainPayload)),
                                PayloadSourceStream = PipeReader.Create(withStream ?
                                    new ReadOnlySequence<byte>(_streamPayload) :
                                    ReadOnlySequence<byte>.Empty)
                            })));
                    return router;
                })
                .BuildServiceProvider();

            var proxy = Proxy.FromConnection(serviceProvider.GetRequiredService<Connection>(), "/");
            var request = new OutgoingRequest(proxy) { Operation = "compressResponsePayload" };

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
                        Assert.That(readResult.Buffer, Has.Length.LessThan(uncompressedLength * CompressionFactor));
                    }
                    else
                    {
                        Assert.That(readResult.Buffer, Has.Length.EqualTo(uncompressedLength));
                    }
                    // Don't consume anything.
                    response.Payload.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    called = true;
                    return response;
                }));

            Assert.That(called, Is.False);
            IncomingResponse response = await pipeline.InvokeAsync(request);
            Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
            Assert.That(called, Is.True);

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
