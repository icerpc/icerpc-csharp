// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public class RelativeProxyTests : ClientServerBaseTest
    {
        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task RelativeProxy_Invocation(Protocol protocol)
        {
            await using var communicator = new Communicator();

            var router = new Router();
            router.Map("/foo", new RelativeProxyOperations());
            router.Map("/foo/bar", new RelativeCallback());
            router.Map("/bar/baz", new RelativeProxy());

            await using var server = new Server
            {
                Communicator = communicator,
                ColocationScope = ColocationScope.None,
                Endpoint = GetTestEndpoint(protocol: protocol),
                Dispatcher = router,
                Protocol = protocol
            };
            _ = server.ListenAndServeAsync();

            var prx1 = IRelativeProxyOperationsPrx.Parse(GetTestProxy("/foo", protocol: protocol), communicator);
            var connection = await prx1.GetConnectionAsync();
            connection.Server = server;

            IRelativeCallbackPrx prx2 = server.CreateRelativeProxy<IRelativeCallbackPrx>("/foo/bar");
            IRelativeProxyPrx prx3 = await prx1.OpRelativeAsync(prx2);
            Assert.AreEqual(2, await prx3.OpAsync());
        }

        public class RelativeProxy : IAsyncRelativeProxy
        {
            private int _count;
            public ValueTask<int> OpAsync(Current current, CancellationToken cancel) => new(++_count);
        }

        public class RelativeCallback : IAsyncRelativeCallback
        {
            public async ValueTask<int> OpAsync(
                IRelativeProxyPrx relative,
                Current current,
                CancellationToken cancel) =>
                await relative.OpAsync(cancel: cancel);
        }

        public class RelativeProxyOperations : IAsyncRelativeProxyOperations
        {
            public async ValueTask<IRelativeProxyPrx> OpRelativeAsync(
                IRelativeCallbackPrx callback,
                Current current,
                CancellationToken cancel)
            {
                Assert.IsNotNull(callback.Connection);
                var relative = current.Server.CreateRelativeProxy<IRelativeProxyPrx>("/bar/baz");
                Assert.AreEqual(1, await callback.OpAsync(relative, cancel: cancel));
                return relative;
            }
        }
    }
}
