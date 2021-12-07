// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.ClientServer
{
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

                return Task.FromResult(new IncomingResponse(request.Protocol, ResultType.Success)
                {
                    Connection = connection, // without a connection, the decoding of response fails, even for void
                    Payload = Encoding.Ice20.CreateEmptyPayload().Span[0],
                    PayloadEncoding = Encoding.Ice20
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

        [TestCase(512, 100, "Optimal")]
        [TestCase(2048, 100, "Optimal")]
        [TestCase(512, 512, "Optimal")]
        [TestCase(2048, 512, "Fastest")]
        [TestCase(512, 2048, "Fastest")]
        [TestCase(2048, 2048, "Fastest")]
        public async Task Compress_Payload(int size, int compressionMinSize, string compressionLevel)
        {
            int compressedRequestSize = 0;
            bool compressedRequest = false;
            int compressedResponseSize = 0;
            bool compressedResponse = false;

            await using ServiceProvider serviceProvider = new IntegrationServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            try
                            {
                                compressedRequestSize = request.Payload.Length;
                                compressedRequest = request.Fields.ContainsKey((int)FieldKey.Compression);
                                OutgoingResponse response = await next.DispatchAsync(request, cancel);
                                compressedResponse = response.Fields.ContainsKey((int)FieldKey.Compression);

                                compressedResponseSize = 0;
                                foreach (ReadOnlyMemory<byte> buffer in response.Payload.ToArray())
                                {
                                    compressedResponseSize += buffer.Length;
                                }

                                return response;
                            }
                            catch
                            {
                                compressedResponse = false;
                                compressedResponseSize = size;
                                throw;
                            }
                        }));
                    router.UseCompressor(
                        new CompressOptions
                        {
                            CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                            CompressionMinSize = compressionMinSize
                        });
                    router.Map<ICompressTest>(new CompressTest());
                    return router;
                })
                .AddTransient<IInvoker>(_ =>
                {
                    var pipeline = new Pipeline();
                    pipeline.UseCompressor(
                        new CompressOptions
                        {
                            CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                            CompressionMinSize = compressionMinSize
                        });
                    return pipeline;
                })
                .BuildServiceProvider();

            CompressTestPrx prx = serviceProvider.GetProxy<CompressTestPrx>();

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
            OutgoingRequest? outgoingRequest = null;
            IncomingRequest? incomingRequest = null;
            OutgoingResponse? outgoingResponse = null;

            await using ServiceProvider serviceProvider = new IntegrationServiceCollection()
                .AddTransient<IInvoker>(_ =>
                {
                    var pipeline = new Pipeline();
                    pipeline.UseCompressor(
                        new CompressOptions
                        {
                            CompressionLevel = Enum.Parse<CompressionLevel>(compressionLevel),
                            CompressionMinSize = compressionMinSize
                        });
                    pipeline.Use(next => new InlineInvoker(
                        (request, cancel) =>
                        {
                            outgoingRequest = request;
                            return next.InvokeAsync(request, cancel);
                        }));
                    return pipeline;
                })
                .AddTransient<IDispatcher>(_ =>
                {
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
                    router.Map<ICompressTest>(new CompressTest());
                    return router;
                }).BuildServiceProvider();

            CompressTestPrx prx = serviceProvider.GetProxy<CompressTestPrx>();

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
