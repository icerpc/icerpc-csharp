// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.ClientServer
{
    // These tests make sure large headers are transmitted correctly.

    [Parallelizable(ParallelScope.All)]
    public class HeaderTests
    {
        [TestCase("ice+tcp://127.0.0.1:0?tls=false")]
        [TestCase("tcp -h 127.0.0.1 -p 0")]
        [TestCase("udp -h 127.0.0.1 -p 0")]
        [TestCase("ice+coloc://header_request:10000")]
        [TestCase("coloc -h header_request -p 10001")]
        public async Task Header_RequestResponseAsync(string endpoint)
        {
            // This large value should be large enough to create multiple buffer for the request and responses headers.
            string largeValue = new('C', 4000);

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<Endpoint>(_ => endpoint)
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<IGreeter>(new Greeter(largeValue));
                    router.Use(next =>
                        new InlineDispatcher(async (request, cancel) =>
                        {
                            OutgoingResponse response = await next.DispatchAsync(request, cancel);
                            if (response.Protocol == Protocol.Ice2 && response.Features.Get<string>() is string value)
                            {
                                response.Fields[1] = encoder => encoder.EncodeString(value);
                            }
                            return response;
                        }));
                    return router;
                })
                .AddTransient<IInvoker>(_ =>
                {
                    var pipeline = new Pipeline();
                    pipeline.Use(next =>
                        new InlineInvoker(async (request, cancel) =>
                        {
                            IncomingResponse response = await next.InvokeAsync(request, cancel);
                            if (response.Fields.Get(1, (ref IceDecoder decoder) => decoder.DecodeString())
                                is string stringValue)
                            {
                                response.Features = new FeatureCollection();
                                response.Features.Set<string>(stringValue);
                            }
                            return response;
                        }));
                    return pipeline;
                })
                .BuildServiceProvider();

            GreeterPrx greeter = serviceProvider.GetProxy<GreeterPrx>();

            var invocation = new Invocation
            {
                Context = new Dictionary<string, string> { ["foo"] = largeValue },
                IsOneway = serviceProvider.GetRequiredService<Endpoint>().Transport == "udp"
            };

            await greeter.SayHelloAsync("hello", invocation);

            if (greeter.Proxy.Protocol == Protocol.Ice2)
            {
                Assert.AreEqual(largeValue, invocation.ResponseFeatures.Get<string>());
            }
        }

        internal class Greeter : Service, IGreeter
        {
            private readonly string _expectedValue;

            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel)
            {
                Assert.AreEqual(_expectedValue, dispatch.Context["foo"]);
                dispatch.ResponseFeatures = new FeatureCollection();
                dispatch.ResponseFeatures.Set<string>(_expectedValue);
                return default;
            }

            internal Greeter(string expectedValue) => _expectedValue = expectedValue;
        }
    }
}
