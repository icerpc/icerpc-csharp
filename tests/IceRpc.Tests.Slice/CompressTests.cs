// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Compression;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice
{
    [Parallelizable(ParallelScope.All)]
    class CompressTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task Compress_Override(bool keepDefault)
        {
            bool executed = false;

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IInvoker>(_ =>
                    new Pipeline().Use(next => new InlineInvoker((request, cancel) =>
                    {
                        executed = true;
                       Assert.That(request.Operation, Is.EqualTo("opCompressArgs"));
                        Assert.AreEqual(keepDefault ? Features.CompressPayload.Yes : Features.CompressPayload.No,
                                        request.Features.Get<Features.CompressPayload>());

                        return next.InvokeAsync(request, cancel);
                    })))
                .BuildServiceProvider();

            CompressTestPrx prx = serviceProvider.GetProxy<CompressTestPrx>();

            var invocation = new Invocation { IsOneway = true };

            if (!keepDefault)
            {
                // The generated code does not and should not override a value set explicitly.
                invocation.Features = invocation.Features.With(Features.CompressPayload.No);
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
            bool compressedRequest = false;
            bool compressedResponse = false;

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            try
                            {
                                compressedRequest = request.Fields.ContainsKey((int)FieldKey.CompressionFormat);
                                OutgoingResponse response = await next.DispatchAsync(request, cancel);
                                compressedResponse = response.Fields.ContainsKey((int)FieldKey.CompressionFormat);
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
                        });
                    return pipeline;
                })
                .BuildServiceProvider();

            CompressTestPrx prx = serviceProvider.GetProxy<CompressTestPrx>();

            byte[] data = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            await prx.OpCompressArgsAsync(size, data);

            Assert.That(compressedRequest, Is.True);
            Assert.That(compressedResponse, Is.False);

            // Both request and response payload should be compressed
            byte[] newData = await prx.OpCompressArgsAndReturnAsync(data);
            Assert.That(compressedRequest, Is.True);
            Assert.That(compressedResponse, Is.True);
           Assert.That(data, Is.EqualTo(newData));

            // The request is not compressed and the response is compressed
            newData = await prx.OpCompressReturnAsync(size);
            Assert.That(compressedRequest, Is.False);
            Assert.That(compressedResponse, Is.True);
           Assert.That(data, Is.EqualTo(newData));

            // The exceptions are never compressed
            Assert.ThrowsAsync<CompressMyException>(async () => await prx.OpWithUserExceptionAsync(size));
            Assert.That(compressedRequest, Is.True);
            Assert.That(compressedResponse, Is.False);
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
                PipeReader stream,
                Dispatch dispatch,
                CancellationToken cancel) => ReadStreamAsync(stream);

            public ValueTask<PipeReader> OpCompressReturnStreamAsync(
                int size,
                Dispatch dispatch,
                CancellationToken cancel) => new(PipeReader.Create(new ReadOnlySequence<byte>(new byte[size])));

            public ValueTask<PipeReader> OpCompressStreamArgAndReturnStreamAsync(
                PipeReader stream,
                Dispatch dispatch,
                CancellationToken cancel) => new(stream);
        }

        private static async ValueTask<int> ReadStreamAsync(PipeReader reader)
        {
            int totalSize = 0;

            while (true)
            {
                ReadResult readResult = await reader.ReadAsync();

                totalSize += (int)readResult.Buffer.Length;

                if (readResult.IsCompleted)
                {
                    break; // while
                }
            }

            return totalSize;
        }
    }
}
