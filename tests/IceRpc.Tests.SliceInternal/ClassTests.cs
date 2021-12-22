// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

using static IceRpc.Slice.Internal.Ice11Definitions;

namespace IceRpc.Tests.SliceInternal
{
    [Timeout(30000)]
    [TestFixture(ProtocolCode.Ice1)]
    [TestFixture(ProtocolCode.Ice2)]
    public sealed class ClassTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly SlicedFormatOperationsPrx _sliced;
        private readonly CompactFormatOperationsPrx _compact;
        private readonly ClassFormatOperationsPrx _classformat;

        public ClassTests(ProtocolCode protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<ISlicedFormatOperations>(new SlicedFormatOperations());
                    router.Map<ICompactFormatOperations>(new CompactFormatOperations());
                    router.Map<IClassFormatOperations>(new ClassFormatOperations());
                    return router;
                })
                .BuildServiceProvider();

            _sliced = SlicedFormatOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
            _compact = CompactFormatOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
            _classformat = ClassFormatOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task Class_FormatMetadata()
        {
            var prx1 = new SlicedFormatOperationsPrx(_sliced.Proxy.Clone());
            var pipeline1 = new Pipeline();
            prx1.Proxy.Invoker = pipeline1;
            pipeline1.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadResult readResult = await request.PayloadSource.ReadAllAsync(cancel);
                ReadOnlyMemory<byte> data = readResult.Buffer.ToArray();
                request.PayloadSource.AdvanceTo(readResult.Buffer.Start);

                DecodeBefore(data);
                IncomingResponse response = await next.InvokeAsync(request, cancel);
                readResult = await response.Payload.ReadAllAsync(cancel);
                Assert.That(readResult.Buffer.IsSingleSegment);
                DecodeAfter(readResult.Buffer.First);

                response.Payload.AdvanceTo(readResult.Buffer.Start);
                return response;

                static void DecodeBefore(ReadOnlyMemory<byte> data)
                {
                    var decoder = new IceDecoder(data, Encoding.Ice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.AreEqual(1, decoder.DecodeSize());
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice includes a size for the sliced format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize));
                }

                static void DecodeAfter(ReadOnlyMemory<byte> data)
                {
                    var decoder = new IceDecoder(data, Encoding.Ice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.AreEqual(1, decoder.DecodeSize());
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice includes a size for the sliced format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize));
                }
            }));
            await prx1.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var prx2 = new CompactFormatOperationsPrx(_compact.Proxy.Clone());
            var pipeline2 = new Pipeline();
            prx2.Proxy.Invoker = pipeline2;
            pipeline2.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadResult readResult = await request.PayloadSource.ReadAllAsync(cancel);
                ReadOnlyMemory<byte> data = readResult.Buffer.ToArray();
                request.PayloadSource.AdvanceTo(readResult.Buffer.Start);

                DecodeBefore(data);
                IncomingResponse response = await next.InvokeAsync(request, cancel);
                readResult = await response.Payload.ReadAllAsync(cancel);
                Assert.That(readResult.Buffer.IsSingleSegment);
                DecodeAfter(readResult.Buffer.First);

                response.Payload.AdvanceTo(readResult.Buffer.Start);
                return response;

                static void DecodeBefore(ReadOnlyMemory<byte> data)
                {
                    var decoder = new IceDecoder(data, Encoding.Ice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.AreEqual(1, decoder.DecodeSize());
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice does not include a size when using the compact format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                }

                static void DecodeAfter(ReadOnlyMemory<byte> data)
                {
                    var decoder = new IceDecoder(data, Encoding.Ice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.AreEqual(1, decoder.DecodeSize());
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice does not include a size when using the compact format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                }
            }));
            await prx2.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var prx3 = new ClassFormatOperationsPrx(_classformat.Proxy.Clone());
            var pipeline3 = new Pipeline();
            prx3.Proxy.Invoker = pipeline3;
            pipeline3.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadResult readResult = await request.PayloadSource.ReadAllAsync(cancel);
                ReadOnlyMemory<byte> data = readResult.Buffer.ToArray();
                request.PayloadSource.AdvanceTo(readResult.Buffer.Start);

                DecodeBefore(data);
                IncomingResponse response = await next.InvokeAsync(request, cancel);
                readResult = await response.Payload.ReadAllAsync(cancel);
                Assert.That(readResult.Buffer.IsSingleSegment);
                DecodeAfter(readResult.Buffer.First);

                response.Payload.AdvanceTo(readResult.Buffer.Start);
                return response;

                static void DecodeBefore(ReadOnlyMemory<byte> data)
                {
                    var decoder = new IceDecoder(data, Encoding.Ice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.AreEqual(1, decoder.DecodeSize());
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice does not include a size when using the compact format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                }

                static void DecodeAfter(ReadOnlyMemory<byte> data)
                {
                    var decoder = new IceDecoder(data, Encoding.Ice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.AreEqual(1, decoder.DecodeSize());
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice does not include a size when using the compact format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                }
            }));
            await prx3.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var pipeline4 = new Pipeline();
            prx3.Proxy.Invoker = pipeline4;
            pipeline4.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadResult readResult = await request.PayloadSource.ReadAllAsync(cancel);
                ReadOnlyMemory<byte> data = readResult.Buffer.ToArray();
                request.PayloadSource.AdvanceTo(readResult.Buffer.Start);

                DecodeBefore(data);
                IncomingResponse response = await next.InvokeAsync(request, cancel);
                readResult = await response.Payload.ReadAllAsync(cancel);
                Assert.That(readResult.Buffer.IsSingleSegment);
                DecodeAfter(readResult.Buffer.First);

                response.Payload.AdvanceTo(readResult.Buffer.Start);
                return response;

                static void DecodeBefore(ReadOnlyMemory<byte> data)
                {
                    var decoder = new IceDecoder(data, Encoding.Ice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.AreEqual(1, decoder.DecodeSize());
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice includes a size for the sliced format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize));
                }

                static void DecodeAfter(ReadOnlyMemory<byte> data)
                {
                    var decoder = new IceDecoder(data, Encoding.Ice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.AreEqual(1, decoder.DecodeSize());
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice includes a size for the sliced format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize));
                }
            }));
            await prx3.OpMyClassSlicedFormatAsync(new MyClassCustomFormat("foo"));
        }

        [TestCase(10, 100, 100)]
        [TestCase(100, 10, 10)]
        [TestCase(50, 200, 10)]
        [TestCase(50, 10, 200)]
        public async Task Class_ClassGraphMaxDepth(
            int graphSize,
            int clientClassGraphMaxDepth,
            int serverClassGraphMaxDepth)
        {
            // We overwrite the default value for class graph max depth through a middleware (server side) and
            // an interceptor (client side).

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(_serviceProvider.GetRequiredService<Connection>().Protocol.Code)
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<IClassGraphOperations>(new ClassGraphOperations());
                    router.Use(next => new InlineDispatcher(
                        (request, cancel) =>
                        {
                            request.Features = request.Features.WithClassGraphMaxDepth(serverClassGraphMaxDepth);
                            return next.DispatchAsync(request, cancel);
                        }));
                    return router;
                })
                .BuildServiceProvider();

            var prx = ClassGraphOperationsPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

            var pipeline = new Pipeline();
            pipeline.Use(next => new InlineInvoker(
                async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    response.Features = response.Features.WithClassGraphMaxDepth(clientClassGraphMaxDepth);

                    return response;
                }));
            prx.Proxy.Invoker = pipeline;

            await prx.IcePingAsync();
            if (graphSize > clientClassGraphMaxDepth)
            {
                Assert.ThrowsAsync<InvalidDataException>(async () => await prx.ReceiveClassGraphAsync(graphSize));
            }
            else
            {
                Assert.DoesNotThrowAsync(async () => await prx.ReceiveClassGraphAsync(graphSize));
            }

            if (graphSize > serverClassGraphMaxDepth)
            {
                Assert.ThrowsAsync<UnhandledException>(
                    async () => await prx.SendClassGraphAsync(CreateClassGraph(graphSize)));
            }
            else
            {
                Assert.DoesNotThrowAsync(async () => await prx.SendClassGraphAsync(CreateClassGraph(graphSize)));
            }
        }

        class CompactFormatOperations : Service, ICompactFormatOperations
        {
            public ValueTask<MyClassCustomFormat> OpMyClassAsync(
                MyClassCustomFormat p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);
        }

        class SlicedFormatOperations : Service, ISlicedFormatOperations
        {
            public ValueTask<MyClassCustomFormat> OpMyClassAsync(
                MyClassCustomFormat p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);
        }

        class ClassFormatOperations : Service, IClassFormatOperations
        {
            public ValueTask<MyClassCustomFormat> OpMyClassAsync(
                MyClassCustomFormat p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask<MyClassCustomFormat> OpMyClassSlicedFormatAsync(
                MyClassCustomFormat p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);
        }

        class ClassGraphOperations : Service, IClassGraphOperations
        {
            public ValueTask<Recursive> ReceiveClassGraphAsync(int size, Dispatch dispatch, CancellationToken cancel) =>
                new(CreateClassGraph(size));
            public ValueTask SendClassGraphAsync(Recursive p1, Dispatch dispatch, CancellationToken cancel) => default;
        }

        private static Recursive CreateClassGraph(int size)
        {
            var root = new Recursive();
            Recursive next = root;
            for (int i = 0; i < size; ++i)
            {
                next.V = new Recursive();
                next = next.V;
            }
            return root;
        }
    }
}
