// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop.ZeroC.Ice;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace IceRpc.Test.Location
{
    public class ServerLocatorRegistry : ITestLocatorRegistry
    {
        private readonly IDictionary<string, IServicePrx> _servers = new ConcurrentDictionary<string, IServicePrx>();
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
                _servers[adapterId] = proxy;
                if (replicaGroupId.Length > 0)
                {
                    _servers[replicaGroupId] = proxy;
                }
            }
            else
            {
                _servers.Remove(adapterId);
                if (replicaGroupId.Length > 0)
                {
                    _servers.Remove(replicaGroupId);
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

        internal IServicePrx? GetAdapter(string server) =>
            _servers.TryGetValue(server, out IServicePrx? proxy) ? proxy : null;

        internal IServicePrx? GetObject(Identity id) =>
            _objects.TryGetValue(id, out IServicePrx? obj) ? obj : null;

        internal void AddObject(IServicePrx obj) => _objects[obj.Identity] = obj;
    }
}
