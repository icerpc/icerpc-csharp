// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

using static IceRpc.Slice.Internal.Slice11Definitions;

namespace IceRpc.Tests.SliceInternal
{
    [Timeout(30000)]
    [TestFixture("ice")]
    [TestFixture("icerpc")]
    public sealed class ClassTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly SlicedFormatOperationsPrx _sliced;
        private readonly CompactFormatOperationsPrx _compact;
        private readonly ClassFormatOperationsPrx _classformat;

        public ClassTests(string protocol)
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

            _sliced.Proxy.Encoding = Encoding.Slice11;
            _compact.Proxy.Encoding = Encoding.Slice11;
            _classformat.Proxy.Encoding = Encoding.Slice11;
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task Class_FormatMetadata()
        {
            var pipeline1 = new Pipeline();
            var prx1 = new SlicedFormatOperationsPrx(_sliced.Proxy with { Invoker = pipeline1 });

            pipeline1.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadResult readResult = await request.PayloadSource.ReadAllAsync(cancel);

                DecodeBefore(readResult.Buffer);
                request.PayloadSource.AdvanceTo(readResult.Buffer.Start);
                IncomingResponse response = await next.InvokeAsync(request, cancel);
                readResult = await response.Payload.ReadAllAsync(cancel);
                DecodeAfter(readResult.Buffer);
                response.Payload.AdvanceTo(readResult.Buffer.Start);

                return response;

                static void DecodeBefore(ReadOnlySequence<byte> data)
                {
                    var decoder = new SliceDecoder(data, Encoding.Slice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.That(decoder.DecodeSize(), Is.EqualTo(1));
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice includes a size for the sliced format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.True);
                }

                static void DecodeAfter(ReadOnlySequence<byte> data)
                {
                    var decoder = new SliceDecoder(data, Encoding.Slice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.That(decoder.DecodeSize(), Is.EqualTo(1));
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice includes a size for the sliced format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.True);
                }
            }));
            await prx1.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var pipeline2 = new Pipeline();
            var prx2 = new CompactFormatOperationsPrx(_compact.Proxy with { Invoker = pipeline2 });
            pipeline2.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadResult readResult = await request.PayloadSource.ReadAllAsync(cancel);
                DecodeBefore(readResult.Buffer);
                request.PayloadSource.AdvanceTo(readResult.Buffer.Start);
                IncomingResponse response = await next.InvokeAsync(request, cancel);
                readResult = await response.Payload.ReadAllAsync(cancel);
                DecodeAfter(readResult.Buffer);
                response.Payload.AdvanceTo(readResult.Buffer.Start);
                return response;

                static void DecodeBefore(ReadOnlySequence<byte> data)
                {
                    var decoder = new SliceDecoder(data, Encoding.Slice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.That(decoder.DecodeSize(), Is.EqualTo(1));
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice does not include a size when using the compact format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                }

                static void DecodeAfter(ReadOnlySequence<byte> data)
                {
                    var decoder = new SliceDecoder(data, Encoding.Slice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.That(decoder.DecodeSize(), Is.EqualTo(1));
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice does not include a size when using the compact format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                }
            }));
            await prx2.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var pipeline3 = new Pipeline();
            var prx3 = new ClassFormatOperationsPrx(_classformat.Proxy with { Invoker = pipeline3 });
            pipeline3.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadResult readResult = await request.PayloadSource.ReadAllAsync(cancel);
                DecodeBefore(readResult.Buffer);
                request.PayloadSource.AdvanceTo(readResult.Buffer.Start);
                IncomingResponse response = await next.InvokeAsync(request, cancel);
                readResult = await response.Payload.ReadAllAsync(cancel);
                DecodeAfter(readResult.Buffer);
                response.Payload.AdvanceTo(readResult.Buffer.Start);
                return response;

                static void DecodeBefore(ReadOnlySequence<byte> data)
                {
                    var decoder = new SliceDecoder(data, Encoding.Slice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.That(decoder.DecodeSize(), Is.EqualTo(1));
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice does not include a size when using the compact format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                }

                static void DecodeAfter(ReadOnlySequence<byte> data)
                {
                    var decoder = new SliceDecoder(data, Encoding.Slice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.That(decoder.DecodeSize(), Is.EqualTo(1));
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
                DecodeBefore(readResult.Buffer);
                request.PayloadSource.AdvanceTo(readResult.Buffer.Start);
                IncomingResponse response = await next.InvokeAsync(request, cancel);
                readResult = await response.Payload.ReadAllAsync(cancel);
                DecodeAfter(readResult.Buffer);

                response.Payload.AdvanceTo(readResult.Buffer.Start);
                return response;

                static void DecodeBefore(ReadOnlySequence<byte> data)
                {
                    var decoder = new SliceDecoder(data, Encoding.Slice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.That(decoder.DecodeSize(), Is.EqualTo(1));
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice includes a size for the sliced format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.True);
                }

                static void DecodeAfter(ReadOnlySequence<byte> data)
                {
                    var decoder = new SliceDecoder(data, Encoding.Slice11);

                    // Skip payload size
                    decoder.Skip(4);

                    // Read the instance marker
                    Assert.That(decoder.DecodeSize(), Is.EqualTo(1));
                    var sliceFlags = (SliceFlags)decoder.DecodeByte();
                    // The Slice includes a size for the sliced format
                    Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.True);
                }
            }));
            await prx3.OpMyClassSlicedFormatAsync(new MyClassCustomFormat("foo"));
        }

        [TestCase(10, 100, 100)]
        [TestCase(100, 10, 10)]
        [TestCase(50, 200, 10)]
        [TestCase(50, 10, 200)]
        public async Task Class_MaxDepth(int graphSize, int clientMaxDepth, int serverMaxDepth)
        {
            // We overwrite the default value for class graph maximum depth through a middleware (server side) and
            // an interceptor (client side).

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(_serviceProvider.GetRequiredService<Connection>().Protocol.Name)
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<IClassGraphOperations>(new ClassGraphOperations());
                    router.UseFeature(new SliceDecodePayloadOptions { MaxDepth = serverMaxDepth });
                    return router;
                })
                .BuildServiceProvider();

            var prx = ClassGraphOperationsPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            prx.Proxy.Encoding = Encoding.Slice11;

            var pipeline = new Pipeline();
            pipeline.UseFeature(new SliceDecodePayloadOptions { MaxDepth = clientMaxDepth });
            prx.Proxy.Invoker = pipeline;

            await new ServicePrx(prx.Proxy).IcePingAsync();
            if (graphSize > clientMaxDepth)
            {
                Assert.ThrowsAsync<InvalidDataException>(async () => await prx.ReceiveClassGraphAsync(graphSize));
            }
            else
            {
                Assert.DoesNotThrowAsync(async () => await prx.ReceiveClassGraphAsync(graphSize));
            }

            if (graphSize > serverMaxDepth)
            {
                DispatchException dispatchException = Assert.ThrowsAsync<DispatchException>(
                    async () => await prx.SendClassGraphAsync(CreateClassGraph(graphSize)));
                Assert.That(dispatchException.ErrorCode, Is.EqualTo(DispatchErrorCode.InvalidData));
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
