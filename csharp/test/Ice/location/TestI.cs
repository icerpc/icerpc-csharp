// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Location
{
    public class TestIntf : ITestIntf
    {
        private Server _server1;
        private Server _server2;
        private ServerLocatorRegistry _registry;

        internal TestIntf(Server server1, Server server2, ServerLocatorRegistry registry)
        {
            _server1 = server1;
            _server2 = server2;
            _registry = registry;

            _registry.AddObject(_server1.Add("hello", new Hello(), IServicePrx.Factory));
            _registry.AddObject(_server1.Add("bonjour#abc", new Hello(), IServicePrx.Factory));
        }

        public void Shutdown(Current current, CancellationToken cancel) =>
            Task.WhenAll(_server1.ShutdownAsync(), _server2.ShutdownAsync());

        public IHelloPrx GetHello(Current current, CancellationToken cancel) =>
            IHelloPrx.Factory.Create(_server1, "hello").Clone(
                location: ImmutableArray.Create(_server1.AdapterId));

        public IHelloPrx GetReplicatedHello(Current current, CancellationToken cancel) =>
            IHelloPrx.Factory.Create(_server1, "hello");

        public void MigrateHello(Current current, CancellationToken cancel)
        {
            var id = Identity.Parse("hello");

            IService? servant = _server1.Remove(id);
            if (servant != null)
            {
                _registry.AddObject(_server2.Add(id, servant, IServicePrx.Factory), current, cancel);
            }
            else
            {
                servant = _server2.Remove(id);
                TestHelper.Assert(servant != null);
                _registry.AddObject(_server1.Add(id, servant, IServicePrx.Factory), current, cancel);
            }
        }
    }
}
