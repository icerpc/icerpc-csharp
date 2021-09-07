// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class ExceptionTagTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;

        public ExceptionTagTests()
        {
            _server = new Server
            {
                Dispatcher = new ExceptionTag(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = _server.Endpoint
            };
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [TestCase("1.1")]
        [TestCase("2.0")]
        public void ExceptionTag_TopLevel(string encoding)
        {
            ExceptionTagPrx prx = GetPrx(encoding);

            var ts = new TaggedExceptionStruct("bar", null);
            TaggedException ex =
                Assert.ThrowsAsync<TaggedException>(async () => await prx.OpTaggedExceptionAsync(null, "foo", ts));
            Assert.AreEqual(false, ex.MBool);
            Assert.That(ex.MInt, Is.Null);
            Assert.AreEqual("foo", ex.MString);
            Assert.That(ex.MStruct, Is.Not.Null);
            Assert.AreEqual(ts, ex.MStruct.Value);
        }

        [TestCase("1.1")]
        [TestCase("2.0")]
        public void ExceptionTag_Derived(string encoding)
        {
            ExceptionTagPrx prx = GetPrx(encoding);

            var ts = new TaggedExceptionStruct("bar", null);

            TaggedException ex;

            if (prx.Proxy.Encoding == Encoding.Ice11)
            {
                DerivedException derivedEx = Assert.ThrowsAsync<DerivedException>(
                    async () => await prx.OpDerivedExceptionAsync(null, "foo", ts));
                ex = derivedEx;

                Assert.AreEqual("foo", derivedEx.MString1);
                Assert.That(derivedEx.MStruct1, Is.Not.Null);
                Assert.AreEqual(ts, derivedEx.MStruct1.Value);
            }
            else
            {
                ex = Assert.ThrowsAsync<TaggedException>(async () => await prx.OpTaggedExceptionAsync(null, "foo", ts));
            }

            Assert.That(ex, Is.Not.Null);

            Assert.AreEqual(false, ex.MBool);
            Assert.That(ex.MInt, Is.Null);
            Assert.AreEqual("foo", ex.MString);
            Assert.That(ex.MStruct, Is.Not.Null);
            Assert.AreEqual(ts, ex.MStruct.Value);
        }

        private ExceptionTagPrx GetPrx(string encoding)
        {
            var prx = ExceptionTagPrx.FromConnection(_connection);
            prx.Proxy.Encoding = Encoding.FromString(encoding);
            return prx;
        }
    }

    public class ExceptionTag : Service, IExceptionTag
    {
        public ValueTask OpDerivedExceptionAsync(
            int? p1,
            string? p2,
            TaggedExceptionStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) => throw new DerivedException(false, p1, p2, p3, p2, p3);

        public ValueTask OpRequiredExceptionAsync(
            int? p1,
            string? p2,
            TaggedExceptionStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) =>
            throw new RequiredException(false, p1, p2, p3, p2 ?? "test", p3 ?? new TaggedExceptionStruct());

        public ValueTask OpTaggedExceptionAsync(
            int? p1,
            string? p2,
            TaggedExceptionStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) => throw new TaggedException(false, p1, p2, p3);
    }
}
