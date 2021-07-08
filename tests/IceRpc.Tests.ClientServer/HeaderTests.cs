// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
                        response.Fields[1] = iceEncoder => iceEncoder.EncodeString(value);
                    }
                    return response;
                }));

            await using var server = new Server
            {
                Dispatcher = router,
                Endpoint = endpoint,
                HostName = "127.0.0.1"
            };
            server.Listen();

            var pipeline = new Pipeline();
            pipeline.Use(next =>
                new InlineInvoker(async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    if (response.Fields.TryGetValue(1, out ReadOnlyMemory<byte> buffer))
                    {
                        response.Features = new FeatureCollection();
                        response.Features.Set<string>(buffer.DecodeFieldValue(iceDecoder => iceDecoder.DecodeString()));
                    }
                    return response;
                }));

            var greeter = IGreeterPrx.FromServer(server);
            await using var connection = new Connection { RemoteEndpoint = greeter.Endpoint };
            greeter.Connection = connection;
            greeter.Invoker = pipeline;

            var invocation = new Invocation
            {
                Context = new Dictionary<string, string> { ["foo"] = largeValue },
                IsOneway = connection.IsDatagram
            };

            await greeter.SayHelloAsync(invocation);

            if (connection.Protocol == Protocol.Ice2)
            {
                Assert.AreEqual(largeValue, invocation.ResponseFeatures.Get<string>());
            }
        }

        internal class Greeter : IGreeter
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
