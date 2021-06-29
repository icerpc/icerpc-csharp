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
            var router = new Router();

            bool? responseFeature = null;

            // This middleare reads the multiplier from the header field (key = 1) and sets a request feature. It also
            // reads an expected response feature.
            router.Use(next => new InlineDispatcher(
                async (request, cancel) =>
                {
                    if (request.Fields.TryGetValue(1, out ReadOnlyMemory<byte> value))
                    {
                        Multiplier multiplier = value.ReadFieldValue(istr => BufferReader.IceReaderIntoInt(istr));
                        if (request.Features.IsReadOnly)
                        {
                            request.Features = new FeatureCollection(request.Features);
                        }
                        request.Features.Set(multiplier);
                    }

                    try
                    {
                        OutgoingResponse response = await next.DispatchAsync(request, cancel);
                        responseFeature = response.Features.Get<bool>();
                        return response;
                    }
                    catch (RemoteException remoteException)
                    {
                        responseFeature = remoteException.Features.Get<bool>();
                        throw;
                    }
                }));
            router.Map<IFeatureTest>(new FeatureTest());

            await using var server = new Server
            {
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
                Dispatcher = router
            };

            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
            var prx = IFeatureTestPrx.FromConnection(connection);

            Multiplier multiplier = 10;

            var pipeline = new Pipeline();
            // This interceptor stores the multiplier into a header field (key = 1) to be read by the middleware.
            pipeline.Use(next => new InlineInvoker(async (request, cancel) =>
            {
                request.Fields.Add(1, writer => writer.WriteInt(multiplier));
                return await next.InvokeAsync(request, cancel);
            }));

            prx.Invoker = pipeline;

            int ret = await prx.ComputeAsync(2);
            Assert.AreEqual(2 * multiplier, ret);
            Assert.That(responseFeature, Is.Not.Null);
            Assert.That(responseFeature!, Is.True);

            responseFeature = null;
            Assert.ThrowsAsync<DispatchException>(async () => await prx.FailWithRemoteAsync());
            Assert.That(responseFeature, Is.Not.Null);
            Assert.That(responseFeature!, Is.True);

            // Features are not provided if the service raises an unhandled exception.
            responseFeature = null;
            Assert.ThrowsAsync<UnhandledException>(async () => await prx.FailWithUnhandledAsync());
            Assert.That(responseFeature, Is.Null);
        }
    }

    public class FeatureTest : IFeatureTest
    {
        public ValueTask<int> ComputeAsync(int value, Dispatch dispatch, CancellationToken cancel)
        {
            if (dispatch.RequestFeatures.Get<Multiplier>() is Multiplier multiplier)
            {
                if (dispatch.ResponseFeatures.IsReadOnly)
                {
                    dispatch.ResponseFeatures = new FeatureCollection(dispatch.ResponseFeatures);
                }
                dispatch.ResponseFeatures.Set(true);
                return new(value * multiplier);
            }
            return new(value);
        }

        public ValueTask FailWithRemoteAsync(Dispatch dispatch, CancellationToken cancel)
        {
            if (dispatch.ResponseFeatures.IsReadOnly)
            {
                dispatch.ResponseFeatures = new FeatureCollection(dispatch.ResponseFeatures);
            }
            dispatch.ResponseFeatures.Set(true);
            throw new DispatchException();
        }

        public ValueTask FailWithUnhandledAsync(Dispatch dispatch, CancellationToken cancel)
        {
            dispatch.ResponseFeatures.Set(true);
            throw new NotImplementedException();
        }

    }
}
