// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ZeroC.Ice.Test.Location
{
    public class ServerLocatorRegistry : ITestLocatorRegistry
    {
        private readonly IDictionary<string, IObjectPrx> _adapters = new ConcurrentDictionary<string, IObjectPrx>();
        private readonly IDictionary<Identity, IObjectPrx> _objects = new ConcurrentDictionary<Identity, IObjectPrx>();

        public void AddObject(IObjectPrx obj, Current current, CancellationToken cancel) => AddObject(obj);

        public void SetAdapterDirectProxy(
            string adapterId,
            IObjectPrx? proxy,
            Current current,
            CancellationToken cancel) =>
            SetReplicatedAdapterDirectProxy(adapterId, "", proxy, current, cancel);

        public void SetReplicatedAdapterDirectProxy(
            string adapterId,
            string replicaGroupId,
            IObjectPrx? proxy,
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

        internal IObjectPrx? GetAdapter(string adapter) =>
            _adapters.TryGetValue(adapter, out IObjectPrx? proxy) ? proxy : null;

        internal IObjectPrx? GetObject(Identity id) =>
            _objects.TryGetValue(id, out IObjectPrx? obj) ? obj : null;

        internal void AddObject(IObjectPrx obj) => _objects[obj.Identity] = obj;
    }
}
