// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Encoding
{
    [Timeout(30000)]
    [TestFixture(Protocol.Ice1)]
    [TestFixture(Protocol.Ice2)]
    public class ClassTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly ISlicedFormatOperationsPrx _sliced;
        private readonly ICompactFormatOperationsPrx _compact;
        private readonly IClassFormatOperationsPrx _classformat;

        public ClassTests(Protocol protocol)
        {
            _communicator = new Communicator();
            var router = new Router();
            router.Map("/sliced", new SlicedFormatOperatinos());
            router.Map("/compact", new CompactFormatOperations());
            router.Map("/classformat", new ClassFormatOperations());

            _server = new Server()
            {
                Dispatcher = router,
                Communicator = _communicator,
                Protocol = protocol
            };
            _ = _server.ListenAndServeAsync();
            _sliced = _server.CreateRelativeProxy<ISlicedFormatOperationsPrx>("/sliced");
            _compact = _server.CreateRelativeProxy<ICompactFormatOperationsPrx>("/compact");
            _classformat = _server.CreateRelativeProxy<IClassFormatOperationsPrx>("/classformat");
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            await _server.ShutdownAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task Class_FormatMetadata()
        {
            var prx1 = _sliced.Clone();
            prx1.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                async (target, request, next, cancel) =>
                {
                    var data = request.Payload.AsArraySegment();
                    var istr = new InputStream(data, prx1.Encoding);

                    (int size, IceRpc.Encoding encoding) = istr.ReadEncapsulationHeader(false);
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
                    var response = await next(target, request, cancel);
                    istr = new InputStream(response.Payload.Slice(1), prx1.Encoding);
                    (size, encoding) = istr.ReadEncapsulationHeader(false);
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
                });
            await prx1.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var prx2 = _compact.Clone();
            prx2.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                async (target, request, next, cancel) =>
                {
                    var data = request.Payload.AsArraySegment();
                    var istr = new InputStream(data, prx2.Encoding);
                    (int size, IceRpc.Encoding encoding) = istr.ReadEncapsulationHeader(false);
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
                    var response = await next(target, request, cancel);
                    istr = new InputStream(response.Payload.Slice(1), prx1.Encoding);
                    (size, encoding) = istr.ReadEncapsulationHeader(false);
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
                });
            await prx2.OpMyClassAsync(new MyClassCustomFormat("foo"));

            var prx3 = _classformat.Clone();
            prx3.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                async (target, request, next, cancel) =>
                {
                    var data = request.Payload.AsArraySegment();
                    var istr = new InputStream(data, prx3.Encoding);
                    (int size, IceRpc.Encoding encoding) = istr.ReadEncapsulationHeader(false);
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
                    var response = await next(target, request, cancel);
                    istr = new InputStream(response.Payload.Slice(1), prx1.Encoding);
                    (size, encoding) = istr.ReadEncapsulationHeader(false);
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
                });
            await prx3.OpMyClassAsync(new MyClassCustomFormat("foo"));

            prx3.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                async (target, request, next, cancel) =>
                {
                    var data = request.Payload.AsArraySegment();
                    var istr = new InputStream(data, prx3.Encoding);
                    (int size, IceRpc.Encoding encoding) = istr.ReadEncapsulationHeader(false);
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
                    var response = await next(target, request, cancel);
                    istr = new InputStream(response.Payload.Slice(1), prx1.Encoding);
                    (size, encoding) = istr.ReadEncapsulationHeader(false);
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
                });
            await prx3.OpMyClassSlicedFormatAsync(new MyClassCustomFormat("foo"));
        }

        [TestCase(10, 100, 100)]
        [TestCase(100, 10, 10)]
        [TestCase(50, 200, 10)]
        [TestCase(50, 10, 200)]
        public async Task Class_ClassGraphMaxDepth(int graphSize, int clientClassGraphMaxDeph, int serverClassGraphMaxDeph)
        {
            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Communicator = communicator,
                ConnectionOptions = new IncomingConnectionOptions()
                {
                    ClassGraphMaxDepth = serverClassGraphMaxDeph
                },
                Dispatcher = new ClassGraphOperations()
            };
            _ = server.ListenAndServeAsync();

            var prx = server.CreateRelativeProxy<IClassGraphOperationsPrx>("/classgraph");
            prx.Connection!.ClassGraphMaxDepth = clientClassGraphMaxDeph;

            if (graphSize > clientClassGraphMaxDeph)
            {
                Assert.ThrowsAsync<InvalidDataException>(async () => await prx.ReceiveClassGraphAsync(graphSize));
            }
            else
            {
                Assert.DoesNotThrowAsync(async () => await prx.ReceiveClassGraphAsync(graphSize));
            }

            if (graphSize > serverClassGraphMaxDeph)
            {
                Assert.ThrowsAsync<UnhandledException>(
                    async () => await prx.SendClassGraphAsync(CreateClassGraph(graphSize)));
            }
            else
            {
                Assert.DoesNotThrowAsync(async () => await prx.SendClassGraphAsync(CreateClassGraph(graphSize)));
            }
        }

        class CompactFormatOperations : IAsyncCompactFormatOperations
        {
            public ValueTask<MyClassCustomFormat> OpMyClassAsync(
                MyClassCustomFormat p1,
                Current current,
                CancellationToken cancel) => new(p1);
        }

        class SlicedFormatOperatinos : IAsyncSlicedFormatOperations
        {
            public ValueTask<MyClassCustomFormat> OpMyClassAsync(
                MyClassCustomFormat p1,
                Current current,
                CancellationToken cancel) => new(p1);
        }

        class ClassFormatOperations : IAsyncClassFormatOperations
        {
            public ValueTask<MyClassCustomFormat> OpMyClassAsync(
                MyClassCustomFormat p1,
                Current current,
                CancellationToken cancel) => new(p1);

            public ValueTask<MyClassCustomFormat> OpMyClassSlicedFormatAsync(
                MyClassCustomFormat p1,
                Current current,
                CancellationToken cancel) => new(p1);
        }

        class ClassGraphOperations : IAsyncClassGraphOperations
        {
            public ValueTask<Recursive> ReceiveClassGraphAsync(int size, Current current, CancellationToken cancel) =>
                new(CreateClassGraph(size));
            public ValueTask SendClassGraphAsync(Recursive p1, Current current, CancellationToken cancel) => default;
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
