// Copyright (c) ZeroC, Inc.

using IceRpc.Ice;
using IceRpc.Ice.Codec;

namespace IceRpc.Features;

/// <summary>Default implementation of <see cref="IIceFeature" />.</summary>
public sealed class IceFeature : IIceFeature
{
    /// <summary>Gets a <see cref="IIceFeature" /> with default values for all properties.</summary>
    public static IIceFeature Default { get; } = new DefaultIceFeature();

    /// <inheritdoc/>
    public IActivator? Activator { get; }

    /// <inheritdoc/>
    public IIceProxy? BaseProxy { get; }

    /// <inheritdoc/>
    public IceEncodeOptions? EncodeOptions { get; }

    /// <inheritdoc/>
    public int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum depth when decoding a class recursively.</summary>
    /// <value>The maximum depth. Defaults to <c>100</c>.</value>
    public int MaxDepth { get; }

    /// <summary>Gets the maximum size of an Ice-encoded payload, in bytes. An Ice-encoded payload corresponds to the
    /// encoded arguments of an operation, or the encoded return values of an operation.</summary>
    /// <value>The maximum size of an Ice-encoded payload, in bytes. Defaults to <c>1</c> MB.</value>
    /// <remarks>The payload size does not include the size of any header for this payload, such as the encapsulation
    /// header with the ice protocol.</remarks>
    public int MaxPayloadSize { get; }

    /// <summary>Constructs an Ice feature.</summary>
    /// <param name="activator">The activator.</param>
    /// <param name="encodeOptions">The encode options.</param>
    /// <param name="maxCollectionAllocation">The maximum collection allocation. Use <c>-1</c> to get the default value:
    /// 8 times <paramref name="maxPayloadSize" /> if set, otherwise the value provided by
    /// <paramref name="defaultFeature" />.</param>
    /// <param name="maxDepth">The maximum depth. Use <c>-1</c> to get the default value.</param>
    /// <param name="maxPayloadSize">The maximum size of an Ice-encoded payload, in bytes. Use <c>-1</c> to get the
    /// default value.</param>
    /// <param name="baseProxy">The base proxy, used when decoding service addresses into proxies.</param>
    /// <param name="defaultFeature">A feature that provides default values for all parameters. <see langword="null" />
    /// is equivalent to <see cref="Default" />.</param>
    public IceFeature(
        IActivator? activator = null,
        IceEncodeOptions? encodeOptions = null,
        int maxCollectionAllocation = -1,
        int maxDepth = -1,
        int maxPayloadSize = -1,
        IIceProxy? baseProxy = null,
        IIceFeature? defaultFeature = null)
    {
        defaultFeature ??= Default;

        Activator = activator ?? defaultFeature.Activator;
        EncodeOptions = encodeOptions ?? defaultFeature.EncodeOptions;

        MaxCollectionAllocation = maxCollectionAllocation >= 0 ? maxCollectionAllocation :
            (maxPayloadSize >= 0 ? 8 * maxPayloadSize : defaultFeature.MaxCollectionAllocation);

        MaxDepth = maxDepth >= 0 ? maxDepth : defaultFeature.MaxDepth;

        MaxPayloadSize = maxPayloadSize >= 0 ? maxPayloadSize : defaultFeature.MaxPayloadSize;

        BaseProxy = baseProxy ?? defaultFeature.BaseProxy;
    }

    private class DefaultIceFeature : IIceFeature
    {
        public IActivator? Activator => null;

        public IIceProxy? BaseProxy => null;

        public IceEncodeOptions? EncodeOptions => null;

        public int MaxCollectionAllocation => 8 * MaxPayloadSize;

        public int MaxDepth => 100;

        public int MaxPayloadSize => 1024 * 1024;
    }
}
