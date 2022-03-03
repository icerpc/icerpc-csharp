// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("1.1")]
    [TestFixture("2.0")]
    public sealed class ExceptionTagTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly Proxy _prx;

        public ExceptionTagTests(string encoding)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, ExceptionTag>()
                .BuildServiceProvider();

            _prx = ExceptionTagPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>()).Proxy;
            _prx.Encoding = Encoding.FromString(encoding);
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public void ExceptionTag_Minus()
        {
            var pipeline = new Pipeline();
            var prx = new ExceptionTagPrx(_prx with { Invoker = pipeline });

            // We decode TaggedException as a TaggedExceptionMinus using a custom activator

            pipeline.UseFeature(new SliceDecodePayloadOptions { Activator = new ActivatorMinus() });

            var ts = new TaggedExceptionStruct("bar", 0);

            TaggedExceptionMinus ex =
                Assert.ThrowsAsync<TaggedExceptionMinus>(async () => await prx.ThrowTaggedExceptionAsync(5, "foo", ts));

            Assert.That(ex.MBool, Is.EqualTo(false));
            Assert.That(ex.MString, Is.EqualTo("foo"));
            Assert.That(ex.MStruct, Is.Not.Null);
            Assert.That(ex.MStruct.Value, Is.EqualTo(ts));
        }

        [Test]
        public void ExceptionTag_Plus()
        {
            var pipeline = new Pipeline();
            var prx = new ExceptionTagPrx(_prx with { Invoker = pipeline });

            // We decode TaggedException as a TaggedExceptionPlus using a custom activator and get a null MFloat

            pipeline.UseFeature(new SliceDecodePayloadOptions { Activator = new ActivatorPlus() });

            var ts = new TaggedExceptionStruct("bar", 0);

            TaggedExceptionPlus ex =
                Assert.ThrowsAsync<TaggedExceptionPlus>(async () => await prx.ThrowTaggedExceptionAsync(null, "foo", ts));

            Assert.That(ex.MFloat, Is.Null);

            Assert.That(ex.MBool, Is.EqualTo(false));
            Assert.That(ex.MInt, Is.Null);
            Assert.That(ex.MString, Is.EqualTo("foo"));
            Assert.That(ex.MStruct, Is.Not.Null);
            Assert.That(ex.MStruct.Value, Is.EqualTo(ts));
        }

        [Test]
        public async Task ExceptionTag_OperationsAsync()
        {
            // Exceptions can't be passed as members with the 1.1 encoding.
            if (_prx.Encoding == Encoding.Slice11)
            {
                return;
            }

            var prx = new ExceptionTagPrx(_prx);
            var tes = new TaggedExceptionStruct("bar", 0);
            var tex = new TaggedException(tes, null, false, "foo");

            var result = await prx.OpTaggedExceptionAsync(tex);

            Assert.That(result.MStruct, Is.Not.Null);
            Assert.That(result.MStruct.Value.S, Is.EqualTo("bar"));
            Assert.That(result.MStruct.Value.V, Is.EqualTo(0));
            Assert.That(result.MInt, Is.Null);
            Assert.That(result.MBool, Is.EqualTo(false));
            Assert.That(result.MString, Is.EqualTo("foo"));
        }

        [Test]
        public void ExceptionTag_Throw()
        {
            var prx = new ExceptionTagPrx(_prx);

            var ts = new TaggedExceptionStruct("bar", 0);

            TaggedException ex =
                Assert.ThrowsAsync<TaggedException>(async () => await prx.ThrowTaggedExceptionAsync(null, "foo", ts));
            CheckException(ex);

            if (prx.Proxy.Encoding == Encoding.Slice11)
            {
                DerivedException derivedEx = Assert.ThrowsAsync<DerivedException>(
                    async () => await prx.ThrowDerivedExceptionAsync(null, "foo", ts));

                Assert.That(derivedEx.MString1, Is.EqualTo("foo"));
                Assert.That(derivedEx.MStruct1, Is.Not.Null);
                Assert.That(derivedEx.MStruct1.Value, Is.EqualTo(ts));
                CheckException(derivedEx);

                RequiredException requiredEx = Assert.ThrowsAsync<RequiredException>(
                    async () => await prx.ThrowRequiredExceptionAsync(null, "foo", ts));

                Assert.That(requiredEx.MString1, Is.EqualTo("foo"));
                Assert.That(requiredEx.MStruct1, Is.EqualTo(ts));
                CheckException(requiredEx);
            }
            else
            {
                ex = Assert.ThrowsAsync<TaggedException>(
                    async () => await prx.ThrowDerivedExceptionAsync(null, "foo", ts));
                CheckException(ex);

                ex = Assert.ThrowsAsync<TaggedException>(
                    async () => await prx.ThrowRequiredExceptionAsync(null, "foo", ts));
                CheckException(ex);
            }

            void CheckException(TaggedException ex)
            {
                Assert.That(ex.MBool, Is.EqualTo(false));
                Assert.That(ex.MInt, Is.Null);
                Assert.That(ex.MString, Is.EqualTo("foo"));
                Assert.That(ex.MStruct, Is.Not.Null);
                Assert.That(ex.MStruct.Value, Is.EqualTo(ts));
            }
        }

        private class ActivatorMinus : IActivator
        {
            public object? CreateInstance(string typeId, ref SliceDecoder decoder)
            {
                Assert.That(typeId, Is.EqualTo(typeof(TaggedException).GetSliceTypeId()));
                return new TaggedExceptionMinus(ref decoder);
            }
        }

        private class ActivatorPlus : IActivator
        {
            public object? CreateInstance(string typeId, ref SliceDecoder decoder)
            {
                Assert.That(typeId, Is.EqualTo(typeof(TaggedException).GetSliceTypeId()));
                return new TaggedExceptionPlus(ref decoder);
            }
        }
    }

    public class ExceptionTag : Service, IExceptionTag
    {
        public ValueTask<TaggedException> OpTaggedExceptionAsync(
            TaggedException p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask ThrowDerivedExceptionAsync(
            int? p1,
            string? p2,
            TaggedExceptionStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) => throw new DerivedException(mStruct: p3,
                                                                    mInt: p1,
                                                                    mBool: false,
                                                                    mString: p2,
                                                                    mString1: p2,
                                                                    mStruct1: p3);

        public ValueTask ThrowRequiredExceptionAsync(
            int? p1,
            string? p2,
            TaggedExceptionStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) =>
            throw new RequiredException(mStruct: p3,
                                        mInt: p1,
                                        mBool: false,
                                        mString: p2,
                                        mString1: p2 ?? "test",
                                        mStruct1: p3 ?? new TaggedExceptionStruct());

        public ValueTask ThrowTaggedExceptionAsync(
            int? p1,
            string? p2,
            TaggedExceptionStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) => throw new TaggedException(mStruct: p3,
                                                                   mInt: p1,
                                                                   mBool: false,
                                                                   mString: p2);
    }
}
