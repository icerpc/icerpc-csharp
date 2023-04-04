// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace IceRpc.Slice;

/// <summary>Base class of all services that implement Slice-defined interfaces.</summary>
public class IceService : Service, IIceObjectService
{
    // A per type cache of type IDs.
    private static readonly ConcurrentDictionary<Type, IReadOnlySet<string>> _cache = new();

    // The service type IDs.
    private readonly IReadOnlySet<string> _typeIds;

    /// <summary>Constructs a new Ice service.</summary>
    public IceService()
    {
        _typeIds = _cache.GetOrAdd(GetType(), type =>
            {
                var typeIds = new SortedSet<string>();
                foreach (Type interfaceType in type.GetInterfaces())
                {
                    typeIds.UnionWith(interfaceType.GetAllSliceTypeIds());
                }
                return typeIds;
            });
    }

    /// <inheritdoc/>
    public virtual ValueTask<IEnumerable<string>> IceIdsAsync(
        IFeatureCollection features,
        CancellationToken cancellationToken) =>
        new(_typeIds);

    /// <inheritdoc/>
    public virtual ValueTask<bool> IceIsAAsync(
        string id,
        IFeatureCollection features,
        CancellationToken cancellationToken) =>
        new(_typeIds.Contains(id));

    /// <inheritdoc/>
    public virtual ValueTask IcePingAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
}
