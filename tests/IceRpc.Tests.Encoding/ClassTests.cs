// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Encoding
{
    [Timeout(30000)]
    [TestFixture(Protocol.Ice1)]
    [TestFixture(Protocol.Ice2)]
    public class ClassTests
    {
        private readonly Server _server;
        private readonly ISlicedFormatOperationsPrx _sliced;
        private readonly ICompactFormatOperationsPrx _compact;
        private readonly IClassFormatOperationsPrx _classformat;

        private readonly Connection _connection;

        public ClassTests(Protocol protocol)
        {
            var router = new Router();
            router.Map<ISlicedFormatOperations>(new SlicedFormatOperations());
            router.Map<ICompactFormatOperations>(new CompactFormatOperations());
            router.Map<IClassFormatOperations>(new ClassFormatOperations());

            _server = new Server()
            {
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint(protocol)
            };
            _server.Listen();

            _connection = new Connection { RemoteEndpoint = _server.ProxyEndpoint };
            // TODO: temporary
            _connection.ConnectAsync().Wait();

            _sliced = ISlicedFormatOperationsPrx.FromConnection(_connection);
            _compact = ICompactFormatOperationsPrx.FromConnection(_connection);
            _classformat = IClassFormatOperationsPrx.FromConnection(_connection);
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            await _server.ShutdownAsync();
            await _connection.ShutdownAsync();
        }

        [Test]
        public async Task Class_FormatMetadata()
        {
            var prx1 = _sliced.Clone();
            var pipeline1 = new Pipeline();
            prx1.Invoker = pipeline1;
            pipeline1.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                var data = request.Payload.AsArraySegment();
                var istr = new InputStream(data, prx1.Encoding);
                if (prx1.Encoding == IceRpc.Encoding.V20)
                {
                    // Read the compression status '0' not compressed
                    Assert.AreEqual(0, istr.ReadByte());
                }
                // Read the instance marker
                Assert.AreEqual(1, istr.ReadSize());
                var sliceFlags = (EncodingDefinitions.SliceFlags)istr.ReadByte();
                // The Slice includes a size for the sliced format
                Assert.That((sliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) != 0, Is.True);

                var response = await next.InvokeAsync(request, cancel);
                istr = new InputStream(await response.GetPayloadAsync(cancel), prx1.Encoding);
                if (prx1.Encoding == IceRpc.Encoding.V20)
                {
                    // Read the compression status '0' not compressed
                    Assert.AreEqual(0, istr.ReadByte());
                }
                // Read the instance marker
                Assert.AreEqual(1, istr.ReadSize());
                sliceFlags = (EncodingDefinitions.SliceFlags)istr.ReadByte();
                // The Slice includes a size for the sliced format
                Assert.That((sliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) != 0, Is.True);
                return response;
            }));
            await prx1.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var prx2 = _compact.Clone();
            var pipeline2 = new Pipeline();
            prx2.Invoker = pipeline2;
            pipeline2.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                var data = request.Payload.AsArraySegment();
                var istr = new InputStream(data, prx2.Encoding);
                if (prx1.Encoding == IceRpc.Encoding.V20)
                {
                    // Read the compression status '0' not compressed
                    Assert.AreEqual(0, istr.ReadByte());
                }
                // Read the instance marker
                Assert.AreEqual(1, istr.ReadSize());
                var sliceFlags = (EncodingDefinitions.SliceFlags)istr.ReadByte();
                // The Slice does not include a size when using the compact format
                Assert.That((sliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) == 0, Is.True);
                var response = await next.InvokeAsync(request, cancel);
                istr = new InputStream(await response.GetPayloadAsync(cancel), prx1.Encoding);
                if (prx1.Encoding == IceRpc.Encoding.V20)
                {
                    // Read the compression status '0' not compressed
                    Assert.AreEqual(0, istr.ReadByte());
                }
                // Read the instance marker
                Assert.AreEqual(1, istr.ReadSize());
                sliceFlags = (EncodingDefinitions.SliceFlags)istr.ReadByte();
                // The Slice does not include a size when using the compact format
                Assert.That((sliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) == 0, Is.True);
                return response;
            }));
            await prx2.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var prx3 = _classformat.Clone();
            var pipeline3 = new Pipeline();
            prx3.Invoker = pipeline3;
            pipeline3.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                var data = request.Payload.AsArraySegment();
                var istr = new InputStream(data, prx3.Encoding);
                if (prx1.Encoding == IceRpc.Encoding.V20)
                {
                    // Read the compression status '0' not compressed
                    Assert.AreEqual(0, istr.ReadByte());
                }
                // Read the instance marker
                Assert.AreEqual(1, istr.ReadSize());
                var sliceFlags = (EncodingDefinitions.SliceFlags)istr.ReadByte();
                // The Slice does not include a size when using the compact format
                Assert.That((sliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) == 0, Is.True);
                var response = await next.InvokeAsync(request, cancel);
                istr = new InputStream(await response.GetPayloadAsync(cancel), prx1.Encoding);
                if (prx1.Encoding == IceRpc.Encoding.V20)
                {
                    // Read the compression status '0' not compressed
                    Assert.AreEqual(0, istr.ReadByte());
                }
                // Read the instance marker
                Assert.AreEqual(1, istr.ReadSize());
                sliceFlags = (EncodingDefinitions.SliceFlags)istr.ReadByte();
                // The Slice does not include a size when using the compact format
                Assert.That((sliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) == 0, Is.True);
                return response;
            }));
            await prx3.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var pipeline4 = new Pipeline();
            prx3.Invoker = pipeline4;
            pipeline4.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                var data = request.Payload.AsArraySegment();
                var istr = new InputStream(data, prx3.Encoding);
                if (prx1.Encoding == IceRpc.Encoding.V20)
                {
                    // Read the compression status '0' not compressed
                    Assert.AreEqual(0, istr.ReadByte());
                }
                // Read the instance marker
                Assert.AreEqual(1, istr.ReadSize());
                var sliceFlags = (EncodingDefinitions.SliceFlags)istr.ReadByte();
                // The Slice includes a size for the sliced format
                Assert.That((sliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) != 0, Is.True);
                var response = await next.InvokeAsync(request, cancel);
                istr = new InputStream(await response.GetPayloadAsync(cancel), prx1.Encoding);
                if (prx1.Encoding == IceRpc.Encoding.V20)
                {
                    // Read the compression status '0' not compressed
                    Assert.AreEqual(0, istr.ReadByte());
                }
                // Read the instance marker
                Assert.AreEqual(1, istr.ReadSize());
                sliceFlags = (EncodingDefinitions.SliceFlags)istr.ReadByte();
                // The Slice includes a size for the sliced format
                Assert.That((sliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) != 0, Is.True);
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
            await using var server = new Server
            {
                ConnectionOptions = new IncomingConnectionOptions()
                {
                    ClassGraphMaxDepth = serverClassGraphMaxDepth
                },
                Dispatcher = new ClassGraphOperations(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            server.Listen();

            await using var connection = new Connection
            {
                RemoteEndpoint = server.ProxyEndpoint,
                Options = new OutgoingConnectionOptions
                {
                    ClassGraphMaxDepth = clientClassGraphMaxDepth
                }
            };

            var prx = IClassGraphOperationsPrx.FromConnection(connection);
            await prx.IcePingAsync();
            Assert.AreEqual(clientClassGraphMaxDepth, connection.ClassGraphMaxDepth);
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

        class CompactFormatOperations : ICompactFormatOperations
        {
            public ValueTask<MyClassCustomFormat> OpMyClassAsync(
                MyClassCustomFormat p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);
        }

        class SlicedFormatOperations : ISlicedFormatOperations
        {
            public ValueTask<MyClassCustomFormat> OpMyClassAsync(
                MyClassCustomFormat p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);
        }

        class ClassFormatOperations : IClassFormatOperations
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

        class ClassGraphOperations : IClassGraphOperations
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
