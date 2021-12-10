// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
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
            var prx = new ExceptionTagPrx(_prx.Clone());

            // We decode TaggedException as a TaggedExceptionMinus using a custom activator
            var pipeline = new Pipeline();
            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                var response = await next.InvokeAsync(request, cancel);

                response.Features = new FeatureCollection(request.Features);
                if (prx.Proxy.Encoding == Encoding.Ice11)
                {
                    response.Features.Set<IIceDecoderFactory<Ice11Decoder>>(
                        new Ice11DecoderFactory(new ActivatorMinus11()));
                }
                else
                {
                    response.Features.Set<IIceDecoderFactory<Ice20Decoder>>(
                        new Ice20DecoderFactory(new ActivatorMinus20()));
                }
                return response;
            }));

            prx.Proxy.Invoker = pipeline;

            var ts = new TaggedExceptionStruct("bar", null);

            TaggedExceptionMinus ex =
                Assert.ThrowsAsync<TaggedExceptionMinus>(async () => await prx.OpTaggedExceptionAsync(5, "foo", ts));

            Assert.AreEqual(false, ex.MBool);
            Assert.AreEqual("foo", ex.MString);
            Assert.That(ex.MStruct, Is.Not.Null);
            Assert.AreEqual(ts, ex.MStruct.Value);
        }

        [Test]
        public void ExceptionTag_Plus()
        {
            var prx = new ExceptionTagPrx(_prx.Clone());

            // We decode TaggedException as a TaggedExceptionPlus using a custom activator and get a null MFloat
            var pipeline = new Pipeline();
            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                var response = await next.InvokeAsync(request, cancel);

                response.Features = new FeatureCollection(request.Features);
                if (prx.Proxy.Encoding == Encoding.Ice11)
                {
                    response.Features.Set<IIceDecoderFactory<Ice11Decoder>>(
                        new Ice11DecoderFactory(new ActivatorPlus11()));
                }
                else
                {
                    response.Features.Set<IIceDecoderFactory<Ice20Decoder>>(
                        new Ice20DecoderFactory(new ActivatorPlus20()));
                }
                return response;
            }));

            prx.Proxy.Invoker = pipeline;

            var ts = new TaggedExceptionStruct("bar", null);

            TaggedExceptionPlus ex =
                Assert.ThrowsAsync<TaggedExceptionPlus>(async () => await prx.OpTaggedExceptionAsync(null, "foo", ts));

            Assert.That(ex.MFloat, Is.Null);

            Assert.AreEqual(false, ex.MBool);
            Assert.That(ex.MInt, Is.Null);
            Assert.AreEqual("foo", ex.MString);
            Assert.That(ex.MStruct, Is.Not.Null);
            Assert.AreEqual(ts, ex.MStruct.Value);
        }

        [Test]
        public void ExceptionTag_Throw()
        {
            var prx = new ExceptionTagPrx(_prx.Clone());

            var ts = new TaggedExceptionStruct("bar", null);

            TaggedException ex =
                Assert.ThrowsAsync<TaggedException>(async () => await prx.OpTaggedExceptionAsync(null, "foo", ts));
            CheckException(ex);

            if (prx.Proxy.Encoding == Encoding.Ice11)
            {
                DerivedException derivedEx = Assert.ThrowsAsync<DerivedException>(
                    async () => await prx.OpDerivedExceptionAsync(null, "foo", ts));

                Assert.AreEqual("foo", derivedEx.MString1);
                Assert.That(derivedEx.MStruct1, Is.Not.Null);
                Assert.AreEqual(ts, derivedEx.MStruct1.Value);
                CheckException(derivedEx);

                RequiredException requiredEx = Assert.ThrowsAsync<RequiredException>(
                    async () => await prx.OpRequiredExceptionAsync(null, "foo", ts));

                Assert.AreEqual("foo", requiredEx.MString1);
                Assert.AreEqual(ts, requiredEx.MStruct1);
                CheckException(requiredEx);
            }
            else
            {
                ex = Assert.ThrowsAsync<TaggedException>(
                    async () => await prx.OpDerivedExceptionAsync(null, "foo", ts));
                CheckException(ex);

                ex = Assert.ThrowsAsync<TaggedException>(
                    async () => await prx.OpRequiredExceptionAsync(null, "foo", ts));
                CheckException(ex);
            }

            void CheckException(TaggedException ex)
            {
                Assert.AreEqual(false, ex.MBool);
                Assert.That(ex.MInt, Is.Null);
                Assert.AreEqual("foo", ex.MString);
                Assert.That(ex.MStruct, Is.Not.Null);
                Assert.AreEqual(ts, ex.MStruct.Value);
            }
        }

        private class ActivatorMinus11 : IActivator<Ice11Decoder>
        {
            public object? CreateInstance(string typeId, Ice11Decoder decoder)
            {
                Assert.AreEqual(typeof(TaggedException).GetIceTypeId(), typeId);
                return new TaggedExceptionMinus(decoder);
            }
        }

        private class ActivatorMinus20 : IActivator<Ice20Decoder>
        {
            public object? CreateInstance(string typeId, Ice20Decoder decoder)
            {
                Assert.AreEqual(typeof(TaggedException).GetIceTypeId(), typeId);
                return new TaggedExceptionMinus(decoder);
            }
        }

        private class ActivatorPlus11 : IActivator<Ice11Decoder>
        {
            public object? CreateInstance(string typeId, Ice11Decoder decoder)
            {
                Assert.AreEqual(typeof(TaggedException).GetIceTypeId(), typeId);
                return new TaggedExceptionPlus(decoder);
            }
        }

        private class ActivatorPlus20 : IActivator<Ice20Decoder>
        {
            public object? CreateInstance(string typeId, Ice20Decoder decoder)
            {
                Assert.AreEqual(typeof(TaggedException).GetIceTypeId(), typeId);
                return new TaggedExceptionPlus(decoder);
            }
        }
    }

    public class ExceptionTag : Service, IExceptionTag
    {
        public ValueTask OpDerivedExceptionAsync(
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

        public ValueTask OpRequiredExceptionAsync(
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

        public ValueTask OpTaggedExceptionAsync(
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
