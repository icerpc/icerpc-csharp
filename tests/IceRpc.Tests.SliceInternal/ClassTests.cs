// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using NUnit.Framework;
using System.IO.Pipelines;

using static IceRpc.Slice.Internal.Ice11Definitions;

namespace IceRpc.Tests.SliceInternal
{
    [Timeout(30000)]
    [TestFixture(ProtocolCode.Ice1)]
    [TestFixture(ProtocolCode.Ice2)]
    public sealed class ClassTests : IAsyncDisposable
    {
        private readonly Server _server;
        private readonly SlicedFormatOperationsPrx _sliced;
        private readonly CompactFormatOperationsPrx _compact;
        private readonly ClassFormatOperationsPrx _classformat;

        private readonly Connection _connection;

        public ClassTests(ProtocolCode protocol)
        {
            var router = new Router();
            router.Map<ISlicedFormatOperations>(new SlicedFormatOperations());
            router.Map<ICompactFormatOperations>(new CompactFormatOperations());
            router.Map<IClassFormatOperations>(new ClassFormatOperations());

            var serverEndpoint = TestHelper.GetUniqueColocEndpoint(Protocol.FromProtocolCode(protocol));
            _server = new Server()
            {
                Dispatcher = router,
                Endpoint = serverEndpoint
            };
            _server.Listen();

            _connection = new Connection
            {
                RemoteEndpoint = _server.Endpoint
            };

            _sliced = SlicedFormatOperationsPrx.FromConnection(_connection);
            _compact = CompactFormatOperationsPrx.FromConnection(_connection);
            _classformat = ClassFormatOperationsPrx.FromConnection(_connection);
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public async Task Class_FormatMetadata()
        {
            var prx1 = new SlicedFormatOperationsPrx(_sliced.Proxy.Clone());
            var pipeline1 = new Pipeline();
            prx1.Proxy.Invoker = pipeline1;
            pipeline1.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadOnlyMemory<byte> data = request.Payload.ToSingleBuffer();
                var decoder = new Ice11Decoder(data);

                // Skip payload size
                decoder.Skip(4);

                // Read the instance marker
                Assert.AreEqual(1, decoder.DecodeSize());
                var sliceFlags = (SliceFlags)decoder.DecodeByte();
                // The Slice includes a size for the sliced format
                Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize));

                IncomingResponse response = await next.InvokeAsync(request, cancel);

                ReadResult readResult = await response.PayloadReader.ReadAsync(cancel);
                Assert.That(readResult.IsCompleted);
                Assert.That(readResult.Buffer.IsSingleSegment);

                decoder = new Ice11Decoder(readResult.Buffer.First);

                // Skip payload size
                decoder.Skip(4);

                // Read the instance marker
                Assert.AreEqual(1, decoder.DecodeSize());
                sliceFlags = (SliceFlags)decoder.DecodeByte();
                // The Slice includes a size for the sliced format
                Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize));
                response.PayloadReader.AdvanceTo(readResult.Buffer.Start);
                return response;
            }));
            await prx1.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var prx2 = new CompactFormatOperationsPrx(_compact.Proxy.Clone());
            var pipeline2 = new Pipeline();
            prx2.Proxy.Invoker = pipeline2;
            pipeline2.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadOnlyMemory<byte> data = request.Payload.ToSingleBuffer();
                var decoder = new Ice11Decoder(data);

                // Skip payload size
                decoder.Skip(4);

                // Read the instance marker
                Assert.AreEqual(1, decoder.DecodeSize());
                var sliceFlags = (SliceFlags)decoder.DecodeByte();
                // The Slice does not include a size when using the compact format
                Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                IncomingResponse response = await next.InvokeAsync(request, cancel);

                ReadResult readResult = await response.PayloadReader.ReadAsync(cancel);
                Assert.That(readResult.IsCompleted);
                Assert.That(readResult.Buffer.IsSingleSegment);

                decoder = new Ice11Decoder(readResult.Buffer.First);

                // Skip payload size
                decoder.Skip(4);

                // Read the instance marker
                Assert.AreEqual(1, decoder.DecodeSize());
                sliceFlags = (SliceFlags)decoder.DecodeByte();
                // The Slice does not include a size when using the compact format
                Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                response.PayloadReader.AdvanceTo(readResult.Buffer.Start);
                return response;
            }));
            await prx2.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var prx3 = new ClassFormatOperationsPrx(_classformat.Proxy.Clone());
            var pipeline3 = new Pipeline();
            prx3.Proxy.Invoker = pipeline3;
            pipeline3.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadOnlyMemory<byte> data = request.Payload.ToSingleBuffer();
                var decoder = new Ice11Decoder(data);

                // Skip payload size
                decoder.Skip(4);

                // Read the instance marker
                Assert.AreEqual(1, decoder.DecodeSize());
                var sliceFlags = (SliceFlags)decoder.DecodeByte();
                // The Slice does not include a size when using the compact format
                Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                IncomingResponse response = await next.InvokeAsync(request, cancel);

                ReadResult readResult = await response.PayloadReader.ReadAsync(cancel);
                Assert.That(readResult.IsCompleted);
                Assert.That(readResult.Buffer.IsSingleSegment);

                decoder = new Ice11Decoder(readResult.Buffer.First);

                // Skip payload size
                decoder.Skip(4);

                // Read the instance marker
                Assert.AreEqual(1, decoder.DecodeSize());
                sliceFlags = (SliceFlags)decoder.DecodeByte();
                // The Slice does not include a size when using the compact format
                Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize), Is.False);
                response.PayloadReader.AdvanceTo(readResult.Buffer.Start);
                return response;
            }));
            await prx3.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var pipeline4 = new Pipeline();
            prx3.Proxy.Invoker = pipeline4;
            pipeline4.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                ReadOnlyMemory<byte> data = request.Payload.ToSingleBuffer();
                var decoder = new Ice11Decoder(data);

                // Skip payload size
                decoder.Skip(4);

                // Read the instance marker
                Assert.AreEqual(1, decoder.DecodeSize());
                var sliceFlags = (SliceFlags)decoder.DecodeByte();
                // The Slice includes a size for the sliced format
                Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize));
                IncomingResponse response = await next.InvokeAsync(request, cancel);

                ReadResult readResult = await response.PayloadReader.ReadAsync(cancel);
                Assert.That(readResult.IsCompleted);
                Assert.That(readResult.Buffer.IsSingleSegment);

                decoder = new Ice11Decoder(readResult.Buffer.First);

                // Skip payload size
                decoder.Skip(4);

                // Read the instance marker
                Assert.AreEqual(1, decoder.DecodeSize());
                sliceFlags = (SliceFlags)decoder.DecodeByte();
                // The Slice includes a size for the sliced format
                Assert.That(sliceFlags.HasFlag(SliceFlags.HasSliceSize));
                response.PayloadReader.AdvanceTo(readResult.Buffer.Start);
                return response;
            }));
            await prx3.OpMyClassSlicedFormatAsync(new MyClassCustomFormat("foo"));
        }

        [TestCase(10, 100, 100)]
        [TestCase(100, 10, 10)]
        [TestCase(50, 200, 10)]
        [TestCase(50, 10, 200)]
        public async Task Class_ClassGraphMaxDepth(int graphSize, int clientClassGraphMaxDepth, int serverClassGraphMaxDepth)
        {
            // We overwrite the default value for class graph max depth through a middleware (server side) and
            // an interceptor (client side).

            var router = new Router();
            router.Map<IClassGraphOperations>(new ClassGraphOperations());
            router.Use(next => new InlineDispatcher(
                (request, cancel) =>
                {
                    request.Features = new FeatureCollection(request.Features);
                    request.Features.Set<IIceDecoderFactory<Ice11Decoder>>(
                        new Ice11DecoderFactory(Ice11Decoder.GetActivator(typeof(ClassTests).Assembly),
                                                serverClassGraphMaxDepth));
                    return next.DispatchAsync(request, cancel);
                }));

            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
            };
            server.Listen();

            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint
            };

            var prx = ClassGraphOperationsPrx.FromConnection(connection);

            var pipeline = new Pipeline();
            pipeline.Use(next => new InlineInvoker(
                async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    response.Features = new FeatureCollection(response.Features);
                    response.Features.Set<IIceDecoderFactory<Ice11Decoder>>(
                         new Ice11DecoderFactory(Ice11Decoder.GetActivator(typeof(ClassTests).Assembly),
                                                 clientClassGraphMaxDepth));

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
