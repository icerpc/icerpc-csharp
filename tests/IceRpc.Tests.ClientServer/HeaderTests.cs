// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
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

            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = endpoint,
                ServerTransport =
                    new ServerTransport().UseColoc().UseTcp().UseUdp()
            };
            server.Listen();

            var pipeline = new Pipeline();
            pipeline.Use(next =>
                new InlineInvoker(async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    if (response.Fields.Get(1, decoder => decoder.DecodeString()) is string stringValue)
                    {
                        response.Features = new FeatureCollection();
                        response.Features.Set<string>(stringValue);
                    }
                    return response;
                }));

            await using var connection = new Connection
            {
                RemoteEndpoint = server.Endpoint,
                ClientTransport = TestHelper.CreateClientTransport(server.Endpoint)
            };
            var greeter = GreeterPrx.FromConnection(connection);
            greeter.Proxy.Invoker = pipeline;

            var invocation = new Invocation
            {
                Context = new Dictionary<string, string> { ["foo"] = largeValue },
                IsOneway = server.Endpoint.Transport == "udp"
            };

            await greeter.SayHelloAsync(invocation);

            if (connection.Protocol == Protocol.Ice2)
            {
                Assert.AreEqual(largeValue, invocation.ResponseFeatures.Get<string>());
            }
        }

        internal class Greeter : Service, IGreeter
        {
            private readonly string _expectedValue;

            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel)
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
