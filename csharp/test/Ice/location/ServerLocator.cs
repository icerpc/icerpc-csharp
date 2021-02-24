// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading;

namespace ZeroC.Ice.Test.Location
{
    public class ServerLocator : ITestLocator
    {
        private readonly ServerLocatorRegistry _registry;
        private readonly ILocatorRegistryPrx _registryPrx;
        private int _requestCount;

        public ServerLocator(ServerLocatorRegistry registry, ILocatorRegistryPrx registryPrx)
        {
            _registry = registry;
            _registryPrx = registryPrx;
            _requestCount = 0;
        }

        public IServicePrx? FindAdapterById(string adapter, Current current, CancellationToken cancel)
        {
            ++_requestCount;
            // We add a small delay to make sure locator request queuing gets tested when
            // running the test on a fast machine
            Thread.Sleep(1);

            return _registry.GetAdapter(adapter);
        }

        public IServicePrx? FindObjectById(Identity id, Current current, CancellationToken cancel)
        {
            ++_requestCount;
            // We add a small delay to make sure locator request queuing gets tested when
            // running the test on a fast machine
            Thread.Sleep(1);

            return _registry.GetObject(id);
        }

        public ILocatorRegistryPrx GetRegistry(Current current, CancellationToken cancel) => _registryPrx;

        public int GetRequestCount(Current current, CancellationToken cancel) => _requestCount;
    }
}
