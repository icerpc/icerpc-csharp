// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class BuiltinOperationsTests : ClientServerBaseTest
    {
        [Test]
        public async Task BuiltinOperations_Invocation()
        {
            await using var communicator = new Communicator();
            var greeter = new GreeterService();
            await using var server = new Server
            {
                Communicator = communicator,
                ColocationScope = ColocationScope.Communicator,
                Dispatcher = greeter
            };
            _ = server.ListenAndServeAsync();

            var context = new Dictionary<string, string> { { "foo", "bar" } };
            IReadOnlyDictionary<string, string> sendContext = new Dictionary<string, string>();
            var prx = server.CreateRelativeProxy<IGreeterTestServicePrx>("/test");
            prx.InvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                async (target, request, next, cancel) =>
                {
                    sendContext = request.Context;
                    await Task.Delay(100, default);
                    return await next(target, request, cancel);
                });

            await prx.IcePingAsync();
            sendContext = new Dictionary<string, string>();
            await prx.IcePingAsync(context);
            CollectionAssert.AreEqual(context, sendContext);
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.IcePingAsync(cancel: new CancellationToken(canceled: true)));

            Assert.AreEqual("::IceRpc::Tests::ClientServer::GreeterTestService", await prx.IceIdAsync());
            sendContext = new Dictionary<string, string>();
            Assert.AreEqual("::IceRpc::Tests::ClientServer::GreeterTestService", await prx.IceIdAsync(context));
            CollectionAssert.AreEqual(context, sendContext);
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.IceIdAsync(cancel: new CancellationToken(canceled: true)));

            var ids = new string[]
            {
                "::Ice::Object",
                "::IceRpc::Tests::ClientServer::GreeterTestService",
            };

            CollectionAssert.AreEqual(ids, await prx.IceIdsAsync());
            sendContext = new Dictionary<string, string>();
            CollectionAssert.AreEqual(ids, await prx.IceIdsAsync(context));
            CollectionAssert.AreEqual(context, sendContext);
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.IceIdsAsync(cancel: new CancellationToken(canceled: true)));

            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::ClientServer::GreeterTestService"), Is.True);
            sendContext = new Dictionary<string, string>();
            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::ClientServer::GreeterTestService", context), Is.True);
            CollectionAssert.AreEqual(context, sendContext);
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.IceIsAAsync(
                    "::IceRpc::Tests::ClientServer::GreeterTestService",
                    cancel: new CancellationToken(canceled: true)));

            var prx1 = prx.As<IServicePrx>();
            Assert.IsNotNull(await prx1.CheckedCastAsync<IGreeterTestServicePrx>());
            sendContext = new Dictionary<string, string>();
            Assert.IsNotNull(await prx1.CheckedCastAsync<IGreeterTestServicePrx>(context));
            CollectionAssert.AreEqual(context, sendContext);
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx1.CheckedCastAsync<IGreeterTestServicePrx>(
                    cancel: new CancellationToken(canceled: true)));
        }

        public class GreeterService : IAsyncGreeterTestService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
