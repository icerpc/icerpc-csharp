// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint,
                Options = new ClientConnectionOptions()
                {
                    ClassFactory = new ClassFactory(new Assembly[] { typeof(RetrySystemFailure).Assembly })
                }
            };
            var prx = CompressTestPrx.FromConnection(connection, invoker: pipeline);

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

        [TestCase(512, 100, "Optimal")]
        [TestCase(2048, 100, "Optimal")]
        [TestCase(512, 512, "Optimal")]
        [TestCase(2048, 512, "Fastest")]
        [TestCase(512, 2048, "Fastest")]
        [TestCase(2048, 2048, "Fastest")]
        public async Task Compress_StreamBytes(int size, int compressionMinSize, string compressionLevel)
        {
            var pipeline = new Pipeline();
            pipeline.Use(Interceptors.CustomCompressor(
                new Interceptors.CompressorOptions
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                    CompressionMinSize = compressionMinSize
                }));

            OutgoingRequest? outgoingRequest = null;
            IncomingRequest? incomingRequest = null;
            OutgoingResponse? outgoingResponse = null;

            pipeline.Use(next => new InlineInvoker(
                (request, cancel) =>
                {
                    outgoingRequest = request;
                    return next.InvokeAsync(request, cancel);
                }));

            var router = new Router();
            router.Use(Middleware.CustomCompressor(
                new Middleware.CompressorOptions
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                    CompressionMinSize = compressionMinSize
                }));
            router.Use(next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    incomingRequest = request;
                    outgoingResponse = await next.DispatchAsync(request, cancel);
                    return outgoingResponse;
                }));

            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Dispatcher = router;
            server.Listen();

            router.Map<ICompressTest>(new CompressTest());
            await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
            var prx = CompressTestPrx.FromConnection(connection, invoker: pipeline);

            using var sendStream = new MemoryStream(new byte[size]);
            int receivedSize = await prx.OpCompressStreamArgAsync(sendStream);
            Assert.That(receivedSize, Is.EqualTo(size));
            Assert.That(outgoingRequest!.StreamCompressor, Is.Not.Null);
            Assert.That(outgoingResponse!.StreamCompressor, Is.Null);
            Assert.That(outgoingRequest!.StreamDecompressor, Is.Not.Null);
            Assert.That(incomingRequest!.StreamDecompressor, Is.Not.Null);

            using var receiveStream = await prx.OpCompressReturnStreamAsync(size);
            Assert.That(ReadStream(receiveStream), Is.EqualTo(size));
            Assert.That(outgoingRequest!.StreamCompressor, Is.Null);
            Assert.That(outgoingResponse!.StreamCompressor, Is.Not.Null);
            Assert.That(outgoingRequest!.StreamDecompressor, Is.Not.Null);
            Assert.That(incomingRequest!.StreamDecompressor, Is.Not.Null);

            byte[] data = new byte[size];
            var random = new Random();
            random.NextBytes(data);
            using var sendStream2 = new MemoryStream(data);
            using var receiveStream2 = await prx.OpCompressStreamArgAndReturnStreamAsync(sendStream2);
            Assert.That(ReadStream(receiveStream2, data), Is.EqualTo(size));
            Assert.That(outgoingRequest!.StreamCompressor, Is.Not.Null);
            Assert.That(outgoingResponse!.StreamCompressor, Is.Not.Null);
            Assert.That(outgoingRequest!.StreamDecompressor, Is.Not.Null);
            Assert.That(incomingRequest!.StreamDecompressor, Is.Not.Null);
        }

        internal class CompressTest : Service, ICompressTest
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

            public ValueTask<int> OpCompressStreamArgAsync(
                Stream stream,
                Dispatch dispatch,
                CancellationToken cancel) => ReadStreamAsync(stream);

            public ValueTask<Stream> OpCompressReturnStreamAsync(
                int size,
                Dispatch dispatch,
                CancellationToken cancel) => new(new MemoryStream(new byte[size]));

            public ValueTask<Stream> OpCompressStreamArgAndReturnStreamAsync(
                Stream stream,
                Dispatch dispatch,
                CancellationToken cancel) => new(stream);
        }

        private static int ReadStream(Stream stream, byte[]? data = null) =>
            ReadStreamAsync(stream, data).AsTask().Result;

        private static async ValueTask<int> ReadStreamAsync(Stream stream, byte[]? data = null)
        {
            int totalSize = 0;
            int received;
            byte[] buffer = new byte[32];

            while ((received = await stream.ReadAsync(buffer)) != 0)
            {
                if (data != null)
                {
                    Assert.That(buffer[0..received], Is.EqualTo(data[totalSize..(totalSize + received)]));
                }
                totalSize += received;
            }
            return totalSize;
        }
    }
}
