// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace ZeroC.Ice
{
    /// <summary>Keeps track of all servers in this process, in order to establish colocated connections.
    /// </summary>
    internal static class LocalServerRegistry
    {
        private static readonly object _mutex = new();
        private static volatile ImmutableList<Server> _serverList = ImmutableList<Server>.Empty;

        internal static Endpoint? GetColocatedEndpoint(ServicePrx proxy) =>
            _serverList.Select(server => server.GetColocatedEndpoint(proxy)).
                FirstOrDefault(endpoint => endpoint != null);

        internal static void RegisterServer(Server server)
        {
            lock (_mutex)
            {
                _serverList = _serverList.Add(server);
            }
        }

        internal static void UnregisterServer(Server server)
        {
            lock (_mutex)
            {
                _serverList = _serverList.Remove(server);
            }
        }
    }
}
