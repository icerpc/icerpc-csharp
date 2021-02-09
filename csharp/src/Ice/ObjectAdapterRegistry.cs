// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace ZeroC.Ice
{
    /// <summary>Keeps track of all object adapters in this application, in order to establish colocated connections.
    /// </summary>
    internal static class ObjectAdapterRegistry
    {
        private static volatile ImmutableList<ObjectAdapter> _objectAdapterList = ImmutableList<ObjectAdapter>.Empty;
        private static readonly object _mutex = new();

        internal static Endpoint? GetColocatedEndpoint(Reference reference) =>
            _objectAdapterList.Select(adapter => adapter.GetColocatedEndpoint(reference)).
                FirstOrDefault(endpoint => endpoint != null);
        internal static void RegisterObjectAdapter(ObjectAdapter adapter)
        {
            lock (_mutex)
            {
                _objectAdapterList = _objectAdapterList.Add(adapter);
            }
        }

        internal static void UnregisterObjectAdapter(ObjectAdapter adapter)
        {
            lock (_mutex)
            {
                _objectAdapterList = _objectAdapterList.Remove(adapter);
            }
        }
    }
}
