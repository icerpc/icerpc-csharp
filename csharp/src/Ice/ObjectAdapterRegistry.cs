// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;

namespace ZeroC.Ice
{
    /// <summary>Keeps track of all object adapters in this application, in order to establish colocated connections.
    /// </summary>
    internal static class ObjectAdapterRegistry
    {
        private static ImmutableArray<ObjectAdapter> _objectAdapterList = ImmutableArray<ObjectAdapter>.Empty;
        private static readonly object _mutex = new();

        internal static Endpoint? GetColocatedEndpoint(Reference reference)
        {
            foreach (ObjectAdapter adapter in _objectAdapterList)
            {
                try
                {
                    // TODO: should work when the communicators don't match but breaks some tests like
                    // IceSSL/configuration.
                    if (adapter.IsLocal(reference) && adapter.Communicator == reference.Communicator)
                    {
                        return adapter.GetColocatedEndpoint();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Ignore.
                }
            }
            return null;
        }

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
