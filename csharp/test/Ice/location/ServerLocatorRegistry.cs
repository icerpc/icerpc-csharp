// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ZeroC.Ice.Test.Location
{
    public class ServerLocatorRegistry : ITestLocatorRegistry
    {
        private readonly IDictionary<string, IServicePrx> _adapters = new ConcurrentDictionary<string, IServicePrx>();
        private readonly IDictionary<Identity, IServicePrx> _objects = new ConcurrentDictionary<Identity, IServicePrx>();

        public void AddObject(IServicePrx obj, Current current, CancellationToken cancel) => AddObject(obj);

        public void SetAdapterDirectProxy(
            string adapterId,
            IServicePrx? proxy,
            Current current,
            CancellationToken cancel) =>
            SetReplicatedAdapterDirectProxy(adapterId, "", proxy, current, cancel);

        public void SetReplicatedAdapterDirectProxy(
            string adapterId,
            string replicaGroupId,
            IServicePrx? proxy,
            Current current,
            CancellationToken cancel)
        {
            if (proxy != null)
            {
                _adapters[adapterId] = proxy;
                if (replicaGroupId.Length > 0)
                {
                    _adapters[replicaGroupId] = proxy;
                }
            }
            else
            {
                _adapters.Remove(adapterId);
                if (replicaGroupId.Length > 0)
                {
                    _adapters.Remove(replicaGroupId);
                }
            }
        }

        public void SetServerProcessProxy(
            string id,
            IProcessPrx? proxy,
            Current current,
            CancellationToken cancel)
        {
            // Ignored
        }

        internal IServicePrx? GetAdapter(string adapter) =>
            _adapters.TryGetValue(adapter, out IServicePrx? proxy) ? proxy : null;

        internal IServicePrx? GetObject(Identity id) =>
            _objects.TryGetValue(id, out IServicePrx? obj) ? obj : null;

        internal void AddObject(IServicePrx obj) => _objects[obj.Identity] = obj;
    }
}
