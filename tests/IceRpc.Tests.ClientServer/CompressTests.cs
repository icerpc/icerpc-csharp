// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    class CompressTests
    {
        [TestCase(512, 100, "Optimal")]
        [TestCase(2048, 100, "Optimal")]
        [TestCase(512, 512, "Optimal")]
        [TestCase(2048, 512, "Fastest")]
        [TestCase(512, 2048, "Fastest")]
        [TestCase(2048, 2048, "Fastest")]
        public async Task Compress_Payload(int size, int compressionMinSize, string compressionLevel)
        {
            var pipeline = new Pipeline();
            pipeline.Use(Interceptors.CustomCompressor(
                new Interceptors.CompressorOptions
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                    CompressionMinSize = compressionMinSize
                }));

            int compressedRequestSize = 0;
            bool compressedRequest = false;
            int compressedResponseSize = 0;

            var router = new Router();
            bool compressedResponse = false;

            router.Use(next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    try
                    {
                        compressedRequestSize = request.PayloadSize;
                        compressedRequest =
                            (await request.GetPayloadAsync(cancel)).Span[0] == (byte)CompressionFormat.Deflate;
                        OutgoingResponse response = await next.DispatchAsync(request, cancel);
                        compressedResponse = response.Payload.Span[0].Span[0] == (byte)CompressionFormat.Deflate;
                        compressedResponseSize = response.PayloadSize;
                        return response;
                    }
                    catch
                    {
                        compressedResponse = false;
                        compressedResponseSize = size;
                        throw;
                    }
                }));
            router.Use(Middleware.CustomCompressor(
                new Middleware.CompressorOptions
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                    CompressionMinSize = compressionMinSize
                }));

            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Dispatcher = router;
            server.Listen();

            router.Map<ICompressTest>(new CompressTest());
            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
            var prx = ICompressTestPrx.FromConnection(connection, invoker: pipeline);

            byte[] data = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            await prx.OpCompressArgsAsync(size, data);

            // Assert the payload is compressed only when it is greater or equal to the connection CompressionMinSize
            // and the compressed payload size is less than the uncompressed size.

            // The request is compressed and the response is not compressed
            if (compressedRequest)
            {
                Assert.That(size, Is.GreaterThanOrEqualTo(compressionMinSize));
                Assert.That(size, Is.GreaterThan(compressedRequestSize));
            }
            else
            {
                Assert.That(size, Is.LessThan(compressionMinSize));
            }
            Assert.That(compressedResponse, Is.False);

            // Both request and response payload should be compressed
            _ = await prx.OpCompressArgsAndReturnAsync(data);
            if (compressedRequest)
            {
                Assert.That(compressedResponse, Is.True);
                Assert.That(size, Is.GreaterThanOrEqualTo(compressionMinSize));
                Assert.That(size, Is.GreaterThan(compressedRequestSize));
                Assert.That(size, Is.GreaterThan(compressedResponseSize));
            }
            else
            {
                Assert.That(size, Is.LessThan(compressionMinSize));
            }

            // The request is not compressed and the response is compressed
            _ = await prx.OpCompressReturnAsync(size);

            Assert.That(compressedRequest, Is.False);
            if (compressedResponse)
            {
                Assert.That(size, Is.GreaterThanOrEqualTo(compressionMinSize));
                Assert.That(size, Is.GreaterThan(compressedResponseSize));
            }
            else
            {
                Assert.That(size, Is.LessThan(compressionMinSize));
            }

            // The exceptions are never compressed
            Assert.ThrowsAsync<CompressMyException>(async () => await prx.OpWithUserExceptionAsync(size));
            Assert.That(compressedRequest, Is.False);
            Assert.That(compressedResponse, Is.False);
        }

        internal class CompressTest : ICompressTest
        {
            public ValueTask<ReadOnlyMemory<byte>> OpCompressArgsAndReturnAsync(
                byte[] p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(p1);

            public ValueTask OpCompressArgsAsync(
                int size,
                byte[] p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                default;

            public ValueTask<ReadOnlyMemory<byte>> OpCompressReturnAsync(
                int size,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(Enumerable.Range(0, size).Select(i => (byte)i).ToArray());

            public ValueTask OpWithUserExceptionAsync(
                int size,
                Dispatch dispatch,
                CancellationToken cancel) =>
                throw new CompressMyException(Enumerable.Range(0, size).Select(i => (byte)i).ToArray());
        }
    }
}
