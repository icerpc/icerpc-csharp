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
            var communicator = new Communicator();
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

        [TestCase("ice+coloc://coloc_duplicate")]
        [TestCase("coloc -h coloc_duplicate")]
        public async Task Coloc_DuplicateAsync(string endpoint)
        {
            var communicator = new Communicator();

            await using var server = new Server
            {
                Communicator = communicator,
                Endpoint = endpoint
            };
            server.Listen();

            await using var duplicate = new Server
            {
                Communicator = communicator,
                Endpoint = endpoint
            };

            Assert.Throws<ArgumentException>(() => duplicate.Listen());
        }

        internal class Greeter : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }
    }
}
