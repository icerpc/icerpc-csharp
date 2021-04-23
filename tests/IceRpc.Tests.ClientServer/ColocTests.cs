// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    // These tests make sure the coloc transport works correctly.

    [Parallelizable(ParallelScope.All)]
    public class ColocTests
    {
        [TestCase("ice+coloc://coloc_connection_refused/foo")]
        [TestCase("foo:coloc -h coloc_connection_refused")]
        public async Task Coloc_ConnectionRefusedAsync(string colocProxy)
        {
            await using var communicator = new Communicator();
            var greeter = IGreeterTestServicePrx.Parse(colocProxy, communicator);

            Assert.ThrowsAsync<ConnectionRefusedException>(async () => await greeter.IcePingAsync());

            await using var server = new Server
            {
                Communicator = communicator,
                Dispatcher = new Greeter(),
                Endpoint = greeter.Endpoints[0].ToString()
            };

            Assert.ThrowsAsync<ConnectionRefusedException>(async () => await greeter.IcePingAsync());
            server.Listen();
            Assert.DoesNotThrowAsync(async () => await greeter.IcePingAsync());
        }

        // Verify that coloc optimization occurs and can be disabled
        [TestCase("ice+tcp://127.0.0.1:0", true)]
        [TestCase("tcp -h 127.0.0.1 -p 0", true)]
        [TestCase("ice+tcp://127.0.0.1:0", false)]
        [TestCase("tcp -h 127.0.0.1 -p 0", false)]
        public async Task Coloc_OptimizationAsync(string endpoint, bool hasColocEndpoint)
        {
            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Communicator = communicator,
                Dispatcher = new Greeter(),
                Endpoint = endpoint,
                HasColocEndpoint = hasColocEndpoint,
                ProxyHost = "localhost"
            };
            server.Listen();

            var greeter = server.CreateProxy<IGreeterTestServicePrx>("/foo");
            Assert.AreEqual(Transport.TCP, greeter.Endpoints[0].Transport);
            Assert.DoesNotThrowAsync(async () => await greeter.IcePingAsync());

            if (hasColocEndpoint)
            {
                Assert.AreEqual(Transport.Coloc, greeter.Connection!.Endpoint.Transport);
            }
            else
            {
                Assert.AreEqual(Transport.TCP, greeter.Connection!.Endpoint.Transport);
            }
        }

        internal class Greeter : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }
    }
}
