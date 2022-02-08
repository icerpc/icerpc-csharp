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
        [TestCase("icerpc://127.0.0.1:0?tls=false")]
        [TestCase("ice://127.0.0.1:0?tls=false")]
        [TestCase("ice://127.0.0.1:0?transport=udp")]
        [TestCase("icerpc://header_request:10000?transport=coloc")]
        [TestCase("ice://header_request:10001?transport=coloc")]
        public async Task Header_RequestResponseAsync(string endpoint)
        {
            // This large value should be large enough to create multiple buffer for the request and responses headers.
            string largeValue = new('C', 4000);

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient(typeof(Endpoint), _ => Endpoint.FromString(endpoint))
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<IGreeter>(new Greeter(largeValue));
                    router.Use(next =>
                        new InlineDispatcher(async (request, cancel) =>
                        {
                            OutgoingResponse response = await next.DispatchAsync(request, cancel);
                            if (response.Protocol == Protocol.IceRpc && request.Features.Get<string>() is string value)
                            {
                                response.FieldsOverrides = response.FieldsOverrides.With(
                                    1,
                                    (ref SliceEncoder encoder) => encoder.EncodeString(value));
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
                            if (response.Fields.DecodeValue(1, (ref SliceDecoder decoder) => decoder.DecodeString())
                                is string stringValue)
                            {
                                request.Features = request.Features.With(stringValue);
                            }
                            return response;
                        }));
                    return pipeline;
                })
                .BuildServiceProvider();

            GreeterPrx greeter = serviceProvider.GetProxy<GreeterPrx>();

            var invocation = new Invocation
            {
                Features = new FeatureCollection().WithContext(new Dictionary<string, string> { ["foo"] = largeValue }),
                IsOneway = serviceProvider.GetRequiredService<Endpoint>().Params.TryGetValue(
                    "transport",
                    out string? transport) && transport == "udp"
            };

            await greeter.SayHelloAsync("hello", invocation);

            if (greeter.Proxy.Protocol == Protocol.IceRpc)
            {
                Assert.AreEqual(largeValue, invocation.Features.Get<string>());
            }
        }

        internal class Greeter : Service, IGreeter
        {
            private readonly string _expectedValue;

            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel)
            {
                Assert.AreEqual(_expectedValue, dispatch.Features.GetContext()["foo"]);
                dispatch.Features = dispatch.Features.With(_expectedValue);
                return default;
            }

            internal Greeter(string expectedValue) => _expectedValue = expectedValue;
        }
    }
}
