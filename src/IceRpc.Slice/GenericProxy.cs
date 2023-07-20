// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>Provides an implementation of <see cref="IProxy" /> that does not implement any Slice interface. It's used
/// to create concrete untyped proxies.</summary>
public readonly record struct GenericProxy : IProxy
{
    /// <inheritdoc/>
    public SliceEncodeOptions? EncodeOptions { get; init; }

    /// <inheritdoc/>
    public IInvoker? Invoker { get; init; }

    /// <inheritdoc/>
    public ServiceAddress ServiceAddress { get; init; }

    /// <summary>Creates a clone of this proxy with the provided service address.</summary>
    /// <param name="serviceAddress">The service address to use for the clone.</param>
    /// <returns>The clone.</returns>
    /// <remarks>When <paramref name="serviceAddress" /> is a relative service address, the service address of the clone
    /// is a clone of the original service address with the path from <paramref name="serviceAddress" />.</remarks>
    /// <seealso cref="ISliceFeature.ProxyFactory" />
    public GenericProxy With(ServiceAddress serviceAddress) =>
        this with
        {
            ServiceAddress = serviceAddress.Protocol is null ?
                ServiceAddress with { Path = serviceAddress.Path } : serviceAddress
        };

    /// <summary>Creates a generic proxy from a proxy struct of type <typeparamref name="TProxy" />.</summary>
    /// <typeparam name="TProxy">The type of the source proxy.</typeparam>
    /// <param name="proxy">The source proxy.</param>
    /// <returns>A new generic proxy.</returns>
    internal static GenericProxy FromProxy<TProxy>(TProxy proxy) where TProxy : struct, IProxy =>
        new()
        {
            EncodeOptions = proxy.EncodeOptions,
            Invoker = proxy.Invoker,
            ServiceAddress = proxy.ServiceAddress
        };
}
