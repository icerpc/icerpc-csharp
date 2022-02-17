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
                            int multiplier = request.Fields.DecodeValue(1, (ref SliceDecoder decoder) => decoder.DecodeInt());
                            if (multiplier != 0) // 0 == default(int)
                            {
                                request.Features = request.Features.With(multiplier);
                            }

                            try
                            {
                                return await next.DispatchAsync(request, cancel);
                            }
                            finally
                            {
                                boolFeature = request.Features.Get<bool>();
                            }
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
                request.FieldsOverrides = request.FieldsOverrides.With(
                    1,
                    (ref SliceEncoder encoder) => encoder.EncodeInt(multiplier));
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
    }
}
