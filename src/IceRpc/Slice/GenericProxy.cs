// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Provides an implementation of <see cref="IProxy" /> that does not implement any Slice interface. It's used
/// to create concrete untyped proxies.</summary>
/// <seealso cref="ISliceFeature.ProxyFactory" />
public readonly record struct GenericProxy : IProxy
{
    /// <inheritdoc/>
    public SliceEncodeOptions? EncodeOptions { get; init; }

    /// <inheritdoc/>
    public IInvoker? Invoker { get; init; }

    /// <inheritdoc/>
    public ServiceAddress ServiceAddress { get; init; }

    /// <summary>Creates a generic proxy from a proxy struct of type <typeparamref name="TProxy" />.
    /// </summary>
    /// <typeparam name="TProxy">The type of the source proxy.</typeparam>
    /// <param name="proxy">The source proxy.</param>
    /// <returns>A new generic proxy.</returns>
    public static GenericProxy FromProxy<TProxy>(TProxy proxy) where TProxy : struct, IProxy =>
        new()
        {
            EncodeOptions = proxy.EncodeOptions,
            Invoker = proxy.Invoker,
            ServiceAddress = proxy.ServiceAddress
        };
}
