// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace IceRpc.Ice;

/// <summary>Provides Ice extension methods for interface <see cref="IProxy" />.</summary>
public static class ProxyExtensions
{
    /// <summary>Tests whether the target service implements the interface implemented by
    /// <typeparamref name="TProxy" />. This method is a wrapper for <see cref="IIceObject.IceIsAAsync" /> and may not
    /// be implemented by all services.</summary>
    /// <typeparam name="TProxy">The type of the target proxy struct.</typeparam>
    /// <param name="proxy">The source proxy being tested.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A new <typeparamref name="TProxy" /> instance, or <see langword="null" />.</returns>
    public static async Task<TProxy?> IceAsAsync<TProxy>(
        this IProxy proxy,
        IFeatureCollection? features = null,
        CancellationToken cancellationToken = default) where TProxy : struct, IProxy =>
        await proxy.ToProxy<IceObjectProxy>().IceIsAAsync(typeof(TProxy).GetSliceTypeId()!, features, cancellationToken)
            .ConfigureAwait(false) ?
            proxy.ToProxy<TProxy>() : null;
}
