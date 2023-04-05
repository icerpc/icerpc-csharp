// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using System.Collections.Concurrent;

namespace IceRpc.Slice;

/// <summary>Base class that implements <see cref="IIceObjectService" /> and for services that implement Slice-defined
/// interfaces.</summary>
public class IceObjectService : Service, IIceObjectService
{
    // A per type cache of type IDs.
    private static readonly ConcurrentDictionary<Type, IReadOnlySet<string>> _cache = new();

    // The service type IDs.
    private readonly IReadOnlySet<string> _typeIds;

    /// <summary>Constructs a new Ice service.</summary>
    public IceObjectService()
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
