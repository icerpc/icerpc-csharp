// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice.Codec;

namespace IceRpc.Ice;

/// <summary>Provides extension methods for <see cref="IIceProxy" /> and generated proxy structs that implement this
/// interface.</summary>
public static class IceProxyExtensions
{
    /// <summary>Converts a proxy into a proxy struct. This conversion always succeeds.</summary>
    /// <typeparam name="TProxy">The type of the target proxy struct.</typeparam>
    /// <param name="proxy">The source proxy.</param>
    /// <returns>A new instance of <typeparamref name="TProxy" />.</returns>
    public static TProxy ToProxy<TProxy>(this IIceProxy proxy) where TProxy : struct, IIceProxy =>
        new() { EncodeOptions = proxy.EncodeOptions, Invoker = proxy.Invoker, ServiceAddress = proxy.ServiceAddress };

    /// <summary>Tests whether the target service implements the Ice interface associated with
    /// <typeparamref name="TProxy" />. This method is a wrapper for <see cref="IIceObject.IceIsAAsync" />.
    /// All services implemented with Ice automatically provide this operation. Services implemented with IceRPC provide
    /// this operation only when they implement Ice interface <c>Ice::Object</c> explicitly.</summary>
    /// <typeparam name="TProxy">The type of the target proxy struct.</typeparam>
    /// <param name="proxy">The source proxy being tested.</param>
    /// <param name="features">The invocation features.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A new <typeparamref name="TProxy" /> instance when <see cref="IIceObject.IceIsAAsync"/> returns
    /// <see langword="true"/>; otherwise, <see langword="null" />.</returns>
    /// <remarks>This method is equivalent to the "checked cast" methods provided by Ice. </remarks>
    public static async Task<TProxy?> AsAsync<TProxy>(
        this IIceProxy proxy,
        IFeatureCollection? features = null,
        CancellationToken cancellationToken = default) where TProxy : struct, IIceProxy =>
        await proxy.ToProxy<IceObjectProxy>().IceIsAAsync(typeof(TProxy).GetIceTypeId()!, features, cancellationToken)
            .ConfigureAwait(false) ?
            proxy.ToProxy<TProxy>() : null;
}
