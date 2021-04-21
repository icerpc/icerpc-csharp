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
        // [TestCase(512, 2048, "Fastest")] TODO: re-enable
        [TestCase(2048, 2048, "Fastest")]
        public async Task Compress_Payload(int size, int compressionMinSize, string compressionLevel)
        {
            await using var communicator = new Communicator
            {
                ConnectionOptions = new OutgoingConnectionOptions()
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                    CompressionMinSize = compressionMinSize
                }
            };

            await using var server = new Server
            {
                Communicator = communicator,
                ConnectionOptions = new IncomingConnectionOptions()
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                    CompressionMinSize = compressionMinSize
                },
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            int compressedRequestSize = 0;
            bool compressedRequest = false;
            int compressedResponseSize = 0;
            bool compressedResponse = false;

            var router = new Router();
            router.Use(next => new InlineDispatcher(
                async (current, cancel) =>
                {
                    try
                    {
                        compressedRequestSize = current.IncomingRequestFrame.PayloadSize;
                        compressedRequest = current.IncomingRequestFrame.HasCompressedPayload;
                        var response = await next.DispatchAsync(current, cancel);
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
            if(compressedResponse)
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

        internal class CompressService : IAsyncCompressService
        {
            public ValueTask<ReadOnlyMemory<byte>> OpCompressArgsAndReturnAsync(
                byte[] p1,
                Current current,
                CancellationToken cancel) =>
                new ValueTask<ReadOnlyMemory<byte>>(p1);

            public ValueTask OpCompressArgsAsync(
                int size,
                byte[] p1,
                Current current,
                CancellationToken cancel) =>
                default;

            public ValueTask<ReadOnlyMemory<byte>> OpCompressReturnAsync(
                int size,
                Current current,
                CancellationToken cancel) =>
                new(Enumerable.Range(0, size).Select(i => (byte)i).ToArray());

            public ValueTask OpWithUserExceptionAsync(
                int size,
                Current current,
                CancellationToken cancel) =>
                throw new CompressMyException(Enumerable.Range(0, size).Select(i => (byte)i).ToArray());
        }
    }
}
