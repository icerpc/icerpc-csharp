// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice.Discovery
{
    /// <summary>Servant class that implements the Slice interface Ice::LocatorRegistry.</summary>
    internal class LocatorRegistry : IAsyncLocatorRegistry
    {
        private readonly Dictionary<string, IServicePrx> _adapters = new();
        private readonly IServicePrx _dummyProxy;
        private readonly object _mutex = new();
        private readonly Dictionary<string, HashSet<string>> _replicaGroups = new();

        public ValueTask SetAdapterDirectProxyAsync(
            string adapterId,
            IServicePrx? proxy,
            Current current,
            CancellationToken cancel) =>
            SetReplicatedAdapterDirectProxyAsync(adapterId, "", proxy, current, cancel);

        public ValueTask SetReplicatedAdapterDirectProxyAsync(
           string adapterId,
           string replicaGroupId,
           IServicePrx? proxy,
           Current current,
           CancellationToken cancel)
        {
            if (adapterId.Length == 0)
            {
                throw new InvalidArgumentException("adapterId cannot be empty", nameof(adapterId));
            }

            lock (_mutex)
            {
                if (proxy != null)
                {

                    _adapters[adapterId] = proxy;
                    if (replicaGroupId.Length > 0)
                    {
                        if (!_replicaGroups.TryGetValue(replicaGroupId, out HashSet<string>? adapterIds))
                        {
                            adapterIds = new();
                            _replicaGroups.Add(replicaGroupId, adapterIds);
                        }
                        adapterIds.Add(adapterId);
                    }
                }
                else
                {
                    _adapters.Remove(adapterId);
                    if (replicaGroupId.Length > 0)
                    {
                        if (_replicaGroups.TryGetValue(replicaGroupId, out HashSet<string>? adapterIds))
                        {
                            adapterIds.Remove(adapterId);
                            if (adapterIds.Count == 0)
                            {
                                _replicaGroups.Remove(replicaGroupId);
                            }
                        }
                    }
                }
            }
            return default;
        }

        public ValueTask SetServerProcessProxyAsync(
            string serverId,
            IProcessPrx process,
            Current current,
            CancellationToken cancel) => default; // Ignored

        internal LocatorRegistry(Communicator communicator) =>
            _dummyProxy = IServicePrx.Parse("dummy", communicator);

        internal (IServicePrx? Proxy, bool IsReplicaGroup) FindAdapter(string adapterId)
        {
            lock (_mutex)
            {
                if (_adapters.TryGetValue(adapterId, out IServicePrx? proxy))
                {
                    return (proxy, false);
                }

                if (_replicaGroups.TryGetValue(adapterId, out HashSet<string>? adapterIds))
                {
                    Debug.Assert(adapterIds.Count > 0);
                    IEnumerable<Endpoint> endpoints = adapterIds.SelectMany(id => _adapters[id].Endpoints);
                    return (_dummyProxy.Clone(endpoints: endpoints), true);
                }

                return (null, false);
            }
        }

        internal async ValueTask<IServicePrx?> FindObjectAsync(Identity identity, CancellationToken cancel)
        {
            if (identity.Name.Length == 0)
            {
                return null;
            }

            var candidates = new List<string>();

            lock (_mutex)
            {
                // We check the local replica groups before the local adapters.
                candidates.AddRange(_replicaGroups.Keys);
                candidates.AddRange(_adapters.Keys);
            }

            foreach (string id in candidates)
            {
                try
                {
                    // This proxy is an indirect proxy with a location (the replica group ID or adapter ID).
                    IServicePrx proxy = _dummyProxy.Clone(
                        IServicePrx.Factory,
                        identity: identity,
                        location: ImmutableArray.Create(id));
                    await proxy.IcePingAsync(cancel: cancel).ConfigureAwait(false);
                    return proxy;
                }
                catch
                {
                    // Ignore and move on to the next replica group ID / adapter ID
                }
            }

            return null;
        }
    }
}
