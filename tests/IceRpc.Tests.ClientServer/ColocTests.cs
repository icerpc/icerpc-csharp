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
        [Test]
        public async Task Coloc_ConnectionRefusedAsync()
        {
            var communicator = new Communicator();
            var greeter = IGreeterTestServicePrx.Parse("ice+coloc://coloc_connection_refused", communicator);

            Assert.ThrowsAsync<ConnectionRefusedException>(async () => await greeter.IcePingAsync());

            await using var server = new Server
            {
                Communicator = communicator,
                Dispatcher = new Greeter(),
                Endpoint = "ice+coloc://coloc_connection_refused"
            };

            Assert.ThrowsAsync<ConnectionRefusedException>(async () => await greeter.IcePingAsync());
            server.Listen();
            Assert.DoesNotThrowAsync(async () => await greeter.IcePingAsync());
        }

        [Test]
        public async Task Coloc_DuplicateAsync()
        {
            var communicator = new Communicator();

            await using var server = new Server
            {
                Communicator = communicator,
                Endpoint = "ice+coloc://coloc_duplicate"
            };
            server.Listen();

            await using var duplicate = new Server
            {
                Communicator = communicator,
                Endpoint = "ice+coloc://coloc_duplicate"
            };

            Assert.Throws<ArgumentException>(() => duplicate.Listen());
        }

        internal class Greeter : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) => default;
        }
    }
}
