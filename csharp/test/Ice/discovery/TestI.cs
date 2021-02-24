// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Discovery
{
    public sealed class Controller : IAsyncController
    {
        private readonly Dictionary<string, Server> _adapters = new();

        private readonly ILocatorRegistryPrx _locatorRegistry;

        public Controller(ILocatorRegistryPrx locatorRegistry) => _locatorRegistry = locatorRegistry;

        public async ValueTask ActivateServerAsync(
            string name,
            string adapterId,
            string replicaGroupId,
            Current current,
            CancellationToken cancel)
        {
            Communicator communicator = current.Communicator;
            bool ice1 = TestHelper.GetTestProtocol(communicator.GetProperties()) == Protocol.Ice1;
            string transport = TestHelper.GetTestTransport(communicator.GetProperties());

            var oa = new Server(
                communicator,
                new()
                {
                    AdapterId = adapterId,
                    Endpoints = ice1 ? $"{transport} -h 127.0.0.1" : $"ice+{transport}://127.0.0.1:0",
                    LocatorRegistry = _locatorRegistry,
                    Name = name,
                    ReplicaGroupId = replicaGroupId,
                    ServerName = "localhost"
                });
            _adapters[name] = oa;
            await oa.ActivateAsync(cancel);
        }

        public async ValueTask DeactivateServerAsync(string name, Current current, CancellationToken cancel)
        {
            await _adapters[name].ShutdownAsync();
            _adapters.Remove(name);
        }

        public ValueTask AddObjectAsync(
            string oaName,
            string identityAndFacet,
            Current current,
            CancellationToken cancel)
        {
            TestHelper.Assert(_adapters.ContainsKey(oaName));
            _adapters[oaName].Add(identityAndFacet, new TestIntf());
            return default;
        }

        public ValueTask RemoveObjectAsync(
            string oaName,
            string identityAndFacet,
            Current current,
            CancellationToken cancel)
        {
            TestHelper.Assert(_adapters.ContainsKey(oaName));
            _adapters[oaName].Remove(identityAndFacet);
            return default;
        }

        public ValueTask ShutdownAsync(Current current, CancellationToken cancel)
        {
            _ = current.Server.ShutdownAsync();
            return default;
        }
    }

    public sealed class TestIntf : ITestIntf
    {
        public string GetAdapterId(Current current, CancellationToken cancel) => current.Server.AdapterId;
    }
}
