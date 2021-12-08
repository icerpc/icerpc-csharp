// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    class CompressTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task Compress_Override(bool keepDefault)
        {
            bool executed = false;

            await using var connection = new Connection(); // dummy connection

            var pipeline = new Pipeline();
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                executed = true;
                Assert.AreEqual("opCompressArgs", request.Operation);
                Assert.AreEqual(keepDefault ? Features.CompressPayload.Yes : Features.CompressPayload.No,
                                request.Features.Get<Features.CompressPayload>());

                return Task.FromResult(
                    new IncomingResponse(
                        request.Protocol,
                        ResultType.Success,
                        PipeReader.Create(ReadOnlySequence<byte>.Empty),
                        Encoding.Ice20)
                    {
                        Connection = connection, // without a connection, the decoding of response fails, even for void
                    });
            }));

            var prx = CompressTestPrx.FromPath(CompressTestPrx.DefaultPath);
            prx.Proxy.Invoker = pipeline;

            Invocation? invocation = null;

            if (!keepDefault)
            {
                // The generated code does not and should not override a value set explicitly.
                invocation = new Invocation();
                invocation.RequestFeatures = invocation.RequestFeatures.With(Features.CompressPayload.No);
            }

            await prx.OpCompressArgsAsync(0, default, invocation);
            Assert.That(executed, Is.True);
        }

        [TestCase(512, "Optimal")]
        [TestCase(2048, "Optimal")]
        [TestCase(512, "Optimal")]
        [TestCase(2048, "Fastest")]
        [TestCase(512, "Fastest")]
        [TestCase(2048, "Fastest")]
        public async Task Compress_Payload(int size, string compressionLevel)
        {
            var pipeline = new Pipeline();
            pipeline.UseCompressor(
                new CompressOptions
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                });

            bool compressedRequest = false;
            bool compressedResponse = false;

            var router = new Router();

            router.Use(next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    try
                    {
                        compressedRequest = request.Fields.ContainsKey((int)FieldKey.Compression);
                        OutgoingResponse response = await next.DispatchAsync(request, cancel);
                        compressedResponse = response.Fields.ContainsKey((int)FieldKey.Compression);

                        return response;
                    }
                    catch
                    {
                        compressedResponse = false;
                        throw;
                    }
                }));
            router.UseCompressor(
                new CompressOptions
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                });

            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            server.Listen();

            router.Map<ICompressTest>(new CompressTest());
            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint
            };
            var prx = CompressTestPrx.FromConnection(connection, invoker: pipeline);

            byte[] data = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            await prx.OpCompressArgsAsync(size, data);

            Assert.That(compressedRequest, Is.True);
            Assert.That(compressedResponse, Is.False);

            // Both request and response payload should be compressed
            byte[] newData = await prx.OpCompressArgsAndReturnAsync(data);
            Assert.That(compressedRequest, Is.True);
            Assert.That(compressedResponse, Is.True);
            CollectionAssert.AreEqual(newData, data);

            // The request is not compressed and the response is compressed
            newData = await prx.OpCompressReturnAsync(size);
            Assert.That(compressedRequest, Is.False);
            Assert.That(compressedResponse, Is.True);
            CollectionAssert.AreEqual(newData, data);

            // The exceptions are never compressed
            Assert.ThrowsAsync<CompressMyException>(async () => await prx.OpWithUserExceptionAsync(size));
            Assert.That(compressedRequest, Is.True);
            Assert.That(compressedResponse, Is.False);
        }

        /*
        TODO: fix & reenable after refactoring outgoing streams
        [TestCase(512, 100, "Optimal")]
        [TestCase(2048, 100, "Optimal")]
        [TestCase(512, 512, "Optimal")]
        [TestCase(2048, 512, "Fastest")]
        [TestCase(512, 2048, "Fastest")]
        [TestCase(2048, 2048, "Fastest")]
        public async Task Compress_StreamBytes(int size, int compressionMinSize, string compressionLevel)
        {
            var pipeline = new Pipeline();
            pipeline.UseCompressor(
                new CompressOptions
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                    CompressionMinSize = compressionMinSize
                });

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
            router.UseCompressor(
                new CompressOptions
                {
                    CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                    CompressionMinSize = compressionMinSize
                });
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
            server.Listen();

            router.Map<ICompressTest>(new CompressTest());
            await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
            var prx = CompressTestPrx.FromConnection(connection, invoker: pipeline);

            using var sendStream = new MemoryStream(new byte[size]);
            int receivedSize = await prx.OpCompressStreamArgAsync(sendStream);
            Assert.That(receivedSize, Is.EqualTo(size));

            using var receiveStream = await prx.OpCompressReturnStreamAsync(size);
            Assert.That(await ReadStreamAsync(receiveStream), Is.EqualTo(size));

            byte[] data = new byte[size];
            var random = new Random();
            random.NextBytes(data);
            using var sendStream2 = new MemoryStream(data);
            using var receiveStream2 = await prx.OpCompressStreamArgAndReturnStreamAsync(sendStream2);
            Assert.That(await ReadStreamAsync(receiveStream2, data), Is.EqualTo(size));
        }

        */

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
