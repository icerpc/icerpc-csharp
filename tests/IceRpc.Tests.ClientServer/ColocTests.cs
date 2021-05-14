// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
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
            var greeter = IGreeterTestServicePrx.Parse(colocProxy);
            await using var connection = new Connection { RemoteEndpoint = greeter.Endpoint };
            greeter.Invoker = connection;

            await using var server = new Server
            {
                Dispatcher = new Greeter(),
                Endpoint = greeter.Endpoint
            };

            Assert.ThrowsAsync<ConnectionRefusedException>(async () => await greeter.IcePingAsync());
            server.Listen();
            Assert.DoesNotThrowAsync(async () => await greeter.IcePingAsync());
        }

        // Verify that coloc optimization occurs and can be disabled
        [TestCase("ice+tcp://127.0.0.1:0?tls=false", true)]
        [TestCase("tcp -h 127.0.0.1 -p 0", true)]
        [TestCase("ice+tcp://127.0.0.1:0?tls=false", false)]
        [TestCase("tcp -h 127.0.0.1 -p 0", false)]
        public async Task Coloc_OptimizationAsync(string endpoint, bool hasColocEndpoint)
        {
            await using var pool = new ConnectionPool();
            await using var server = new Server
            {
                Invoker = pool,
                Dispatcher = new Greeter(),
                Endpoint = endpoint,
                HasColocEndpoint = hasColocEndpoint,
                ProxyHost = "localhost"
            };
            server.Listen();

            var greeter = IGreeterTestServicePrx.FromServer(server, "/foo");
            Assert.AreEqual(Transport.TCP, greeter.Endpoint!.Transport);
            Assert.DoesNotThrowAsync(async () => await greeter.IcePingAsync());

            if (hasColocEndpoint)
            {
                Assert.AreEqual(Transport.Coloc, greeter.Connection!.Transport);
            }
            else
            {
                Assert.AreEqual(Transport.TCP, greeter.Connection!.Transport);
            }
        }

        internal class Greeter : IGreeterTestService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
