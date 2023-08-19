// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using System.Diagnostics.CodeAnalysis;
using ZeroC.Slice;

namespace IceRpc.Slice.Ice;

/// <remarks>The IceRpc + Slice integration provides a default implementation for all methods of this interface.
/// </remarks>
[SuppressMessage(
    "StyleCop.CSharp.DocumentationRules",
    "SA1605:PartialElementDocumentationMustHaveSummary",
    Justification = "Use generated summary.")]
public partial interface IIceObjectService
{
    /// <summary>Gets the Slice type IDs of all the interfaces implemented by the target service.</summary>
    /// <param name="features">The dispatch features.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The Slice type IDs of all these interfaces, sorted alphabetically.</returns>
    public ValueTask<IEnumerable<string>> IceIdsAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        var sortedSet = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Type type in GetType().GetInterfaces())
        {
            if (type.GetSliceTypeId() is string typeId)
            {
                sortedSet.Add(typeId);
            }
        }
        return new(sortedSet);
    }

    /// <summary>Tests whether the target service implements the specified interface.</summary>
    /// <param name="id">The Slice type ID of the interface to test against.</param>
    /// <param name="features">The dispatch features.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>True when the target service implements this interface; otherwise, false.</returns>
    public ValueTask<bool> IceIsAAsync(
        string id,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        foreach (Type type in GetType().GetInterfaces())
        {
            if (type.GetSliceTypeId() is string typeId && typeId == id)
            {
                return new(true);
            }
        }
        return new(false);
    }

    /// <summary>Pings the service.</summary>
    /// <param name="features">The dispatch features.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task that completes when this implementation completes.</returns>
    public ValueTask IcePingAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
}
