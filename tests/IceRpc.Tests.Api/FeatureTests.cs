// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

using Multiplier = System.Int32;

namespace IceRpc.Tests.Api
{
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
            var router = new Router();

            bool? responseFeature = null;

            // This middleare reads the multiplier from the binary context and sets a request feature. It also
            // reads an expected response feature.
            router.Use(next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    if (request.BinaryContext.TryGetValue(1, out ReadOnlyMemory<byte> value))
                    {
                        Multiplier multiplier = value.Read(istr => InputStream.IceReaderIntoInt(istr));
                        request.Features.Set(multiplier);
                    }
                    OutgoingResponse response = await next.DispatchAsync(request, cancel);

                    responseFeature = response.Features.Get<bool>();

                    return response;
                }));
            router.Mount("/test", new FeatureService());

            await using var communicator = new Communicator();

            await using var server = new Server
            {
                Invoker = communicator,
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = router
            };

            server.Listen();

            var prx = IFeatureServicePrx.FromServer(server, "/test");

            Multiplier multiplier = 10;
            // This interceptor stores the multiplier into the binary context to be read by the middleware.
            communicator.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                request.BinaryContextOverride.Add(1, ostr => ostr.WriteInt(multiplier));
                return await next.InvokeAsync(request, cancel);
            }));

            int ret = await prx.ComputeAsync(2);
            Assert.AreEqual(2 * multiplier, ret);
            Assert.That(responseFeature, Is.Not.Null);
            Assert.That(responseFeature!, Is.True);

            responseFeature = null;
            Assert.ThrowsAsync<UnhandledException>(async () => await prx.FailAsync());
            Assert.That(responseFeature, Is.Not.Null);
            Assert.That(responseFeature!, Is.True);
        }
    }

    public class FeatureService : IFeatureService
    {
        public ValueTask<int> ComputeAsync(int value, Dispatch dispatch, CancellationToken cancel)
        {
            if (dispatch.RequestFeatures.Get<Multiplier>() is Multiplier multiplier)
            {
                dispatch.ResponseFeatures.Set(true);
                return new(value * multiplier);
            }
            return new(value);
        }

        public ValueTask FailAsync(Dispatch dispatch, CancellationToken cancel)
        {
            dispatch.ResponseFeatures.Set(true);
            throw new NotImplementedException();
        }

    }
}
