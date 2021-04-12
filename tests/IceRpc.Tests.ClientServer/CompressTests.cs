// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

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
            var properties = new Dictionary<string, string>
            {
                { "Ice.CompressionMinSize", $"{compressionMinSize}" },
                { "Ice.CompressionLevel", compressionLevel }
            };

            await using var communicator = new Communicator(properties);
            await using var server = new Server { Communicator = communicator };

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
                        compressedRequestSize = response.PayloadSize;
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
            _ = server.ListenAndServeAsync();

            router.Map("/compress", new CompressService());
            var prx = ICompressServicePrx.Factory.Create(server, "/compress");

            byte[] data = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            await prx.OpCompressArgsAsync(size, data);

            // Assert the payload is compressed only when it is greater or equal to Ice.CompressionMinSize
            // and the compressed payload size is less than the uncompressed size.

            Assert.IsTrue(compressedRequest || size < compressionMinSize);
            Assert.IsTrue(!compressedRequest || size > compressedRequestSize);
            Assert.IsFalse(compressedResponse);

            _ = await prx.OpCompressArgsAndReturnAsync(data);

            Assert.IsTrue(compressedRequest || size < compressionMinSize);
            Assert.IsTrue(!compressedRequest || size > compressedRequestSize);

            Assert.IsTrue(compressedResponse || size < compressionMinSize);
            Assert.IsTrue(!compressedResponse || size > compressedResponseSize);

            _ = await prx.OpCompressReturnAsync(size);

            Assert.IsFalse(compressedRequest);
            Assert.IsTrue(compressedResponse || size < compressionMinSize);
            Assert.IsTrue(compressedResponse || size > compressedResponseSize);

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
