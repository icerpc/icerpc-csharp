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
    public class ClassFormatTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly ISlicedFormatOperationsPrx _prx1;
        private readonly ICompactFormatOperationsPrx _prx2;
        private readonly IClassFormatOperationsPrx _prx3;

        public ClassFormatTests(Protocol protocol)
        {
            _communicator = new Communicator();
            var router = new Router();
            router.Map("/sliced", new SlicedFormatOperatinos());
            router.Map("/compact", new CompactFormatOperations());
            router.Map("/classformat", new ClassFormatOperations());

            _server = new Server
            {
                Dispatcher = router,
                Communicator = _communicator,
                Protocol = protocol
            };
            _ = _server.ListenAndServeAsync();
            _prx1 = _server.CreateRelativeProxy<ISlicedFormatOperationsPrx>("/sliced");
            _prx2 = _server.CreateRelativeProxy<ICompactFormatOperationsPrx>("/compact");
            _prx3 = _server.CreateRelativeProxy<IClassFormatOperationsPrx>("/classformat");
        }

        [OneTimeTearDown]
        public async Task TearDown()
        {
            await _server.ShutdownAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public async Task ClassFormat_Metadata()
        {
            var prx1 = _prx1.Clone();
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

            var prx2 = _prx2.Clone();
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

            var prx3 = _prx3.Clone();
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
    }
}
