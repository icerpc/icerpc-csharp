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
            await using var pool = new Communicator { };
            pool.Use(Interceptor.CreateCompressor(CompressionFormat.GZip,
                                                  Enum.Parse<CompressionLevel>(compressionLevel),
                                                  compressionMinSize));
            pool.Use(Interceptor.Decompressor);

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
                        compressedRequest = request.HasCompressedPayload;
                        var response = await next.DispatchAsync(request, cancel);
                        compressedResponse = response.HasCompressedPayload;
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
            router.Use(Middleware.CreateCompressor(CompressionFormat.GZip,
                                                   Enum.Parse<CompressionLevel>(compressionLevel),
                                                   compressionMinSize));
            router.Use(Middleware.Decompressor);
            await using var server = new Server
            {
                Invoker = pool,
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Dispatcher = router;
            server.Listen();

            router.Map("/compress", new CompressService());
            ICompressServicePrx prx = server.CreateProxy<ICompressServicePrx>("/compress");

            byte[] data = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            await prx.OpCompressArgsAsync(size, data);

            // Assert the payload is compressed only when it is greater or equal to the connection CompressionMinSize
            // and the compressed payload size is less than the uncompressed size.

            // The request is compressed and the response is not compressed
            if (compressedRequest)
            {
                Assert.GreaterOrEqual(size, compressionMinSize);
                Assert.Greater(size, compressedRequestSize);
            }
            else
            {
                Assert.IsTrue(size < compressionMinSize);
            }
            Assert.IsFalse(compressedResponse);

            // Both request and response payload should be compressed
            _ = await prx.OpCompressArgsAndReturnAsync(data);
            if (compressedRequest)
            {
                Assert.IsTrue(compressedResponse);
                Assert.GreaterOrEqual(size, compressionMinSize);
                Assert.Greater(size, compressedRequestSize);
                Assert.Greater(size, compressedResponseSize);
            }
            else
            {
                Assert.IsTrue(size < compressionMinSize);
            }

            // The request is not compressed and the response is compressed
            _ = await prx.OpCompressReturnAsync(size);

            Assert.IsFalse(compressedRequest);
            if (compressedResponse)
            {
                Assert.GreaterOrEqual(size, compressionMinSize);
                Assert.Greater(size, compressedResponseSize);
            }
            else
            {
                Assert.IsTrue(size < compressionMinSize);
            }

            // The exceptions are never compressed
            Assert.ThrowsAsync<CompressMyException>(async () => await prx.OpWithUserExceptionAsync(size));
            Assert.IsFalse(compressedRequest);
            Assert.IsFalse(compressedResponse);
        }

        internal class CompressService : ICompressService
        {
            public ValueTask<ReadOnlyMemory<byte>> OpCompressArgsAndReturnAsync(
                byte[] p1,
                Dispatch dispatch,
                CancellationToken cancel) =>
                new ValueTask<ReadOnlyMemory<byte>>(p1);

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
