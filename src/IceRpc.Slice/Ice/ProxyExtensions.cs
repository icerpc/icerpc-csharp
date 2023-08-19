// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using ZeroC.Slice;

namespace IceRpc.Slice.Ice;

/// <summary>Provides an extension method for interface <see cref="IProxy" />.</summary>
public static class ProxyExtensions
{
    /// <summary>Tests whether the target service implements the interface implemented by
    /// <typeparamref name="TProxy" />. This method is a wrapper for <see cref="IIceObject.IceIsAAsync" />.
    /// All services implemented with Ice automatically provide this operation. Services implemented with IceRPC provide
    /// this operation only when they implement Slice interface <c>Ice::Object</c> explicitly.</summary>
    /// <typeparam name="TProxy">The type of the target proxy struct.</typeparam>
    /// <param name="proxy">The source proxy being tested.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A new <typeparamref name="TProxy" /> instance, or <see langword="null" />.</returns>
    public static async Task<TProxy?> AsAsync<TProxy>(
        this IProxy proxy,
        IFeatureCollection? features = null,
        CancellationToken cancellationToken = default) where TProxy : struct, IProxy =>
        await proxy.ToProxy<IceObjectProxy>().IceIsAAsync(typeof(TProxy).GetSliceTypeId()!, features, cancellationToken)
            .ConfigureAwait(false) ?
            proxy.ToProxy<TProxy>() : null;
}
