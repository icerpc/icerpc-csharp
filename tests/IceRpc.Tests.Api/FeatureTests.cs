// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Multiplier = System.Int32;

namespace IceRpc.Tests.Api
{
    [Timeout(30000)]
    public class FeatureTests
    {
        [Test]
        public void FeatureCollection_GetSet()
        {
            var features = new FeatureCollection();

            Assert.That(features.Get<string>(), Is.Null);

            features.Set("foo");
            string? s = features.Get<string>();
            Assert.That(s, Is.Not.Null);
            Assert.AreEqual("foo", s!);

            // Test defaults
            var features2 = new FeatureCollection(features);

            Assert.AreEqual("foo", features2.Get<string>());
            features2.Set("bar");
            Assert.AreEqual("foo", features.Get<string>());
            Assert.AreEqual("bar", features2.Get<string>());

            features2.Set<string>(null);
            Assert.AreEqual("foo", features.Get<string>());
            Assert.AreEqual("foo", features2.Get<string>());
        }

        [Test]
        public void FeatureCollection_Index()
        {
            var features = new FeatureCollection();

            Assert.That(features[typeof(int)], Is.Null);

            features[typeof(int)] = 42;
            Assert.AreEqual(42, (int)features[typeof(int)]!);
        }

        [Test]
        public async Task Dispatch_Features()
        {
            bool? boolFeature = null;
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    // This middleare reads the multiplier from the header field (key = 1) and sets a request feature. It also
                    // reads an expected response feature.
                    var router = new Router();
                    router.Use(next => new InlineDispatcher(
                        async (request, cancel) =>
                        {
                            int multiplier = request.Fields.Get(1, (ref SliceDecoder decoder) => decoder.DecodeInt());
                            if (multiplier != 0) // 0 == default(int)
                            {
                                if (request.Features.IsReadOnly)
                                {
                                    request.Features = new FeatureCollection(request.Features);
                                }
                                request.Features.Set(multiplier);
                            }

                            OutgoingResponse response = await next.DispatchAsync(request, cancel);
                            boolFeature = request.Features.Get<bool>();
                            return response;
                        }));
                    router.Map<IFeatureTest>(new FeatureTest());
                    return router;
                })
                .BuildServiceProvider();

            var prx = FeatureTestPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

            Multiplier multiplier = 10;

            var pipeline = new Pipeline();
            // This interceptor stores the multiplier into a header field (key = 1) to be read by the middleware.
            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                request.Fields.Add(1, (ref SliceEncoder encoder) => encoder.EncodeInt(multiplier));
                return await next.InvokeAsync(request, cancel);
            }));

            prx.Proxy.Invoker = pipeline;

            int ret = await prx.ComputeAsync(2);
            Assert.AreEqual(2 * multiplier, ret);
            Assert.That(boolFeature, Is.Not.Null);
            Assert.That(boolFeature!, Is.True);

            boolFeature = null;
            Assert.ThrowsAsync<DispatchException>(async () => await prx.FailWithRemoteAsync());
            Assert.That(boolFeature, Is.Not.Null);
            Assert.That(boolFeature!, Is.True);

            // Features are not provided if the service raises an unhandled exception.
            boolFeature = null;
            Assert.ThrowsAsync<UnhandledException>(async () => await prx.FailWithUnhandledAsync());
            Assert.That(boolFeature, Is.Null);
        }
    }

    public class FeatureTest : Service, IFeatureTest
    {
        public ValueTask<int> ComputeAsync(int value, Dispatch dispatch, CancellationToken cancel)
        {
            if (dispatch.Features.Get<Multiplier>() is Multiplier multiplier)
            {
                dispatch.Features = dispatch.Features.With(true);
                return new(value * multiplier);
            }
            return new(value);
        }

        public ValueTask FailWithRemoteAsync(Dispatch dispatch, CancellationToken cancel)
        {
            dispatch.Features = dispatch.Features.With(true);
            throw new DispatchException();
        }

        public ValueTask FailWithUnhandledAsync(Dispatch dispatch, CancellationToken cancel)
        {
            dispatch.Features = dispatch.Features.With(true);
            throw new NotImplementedException();
        }

    }
}
