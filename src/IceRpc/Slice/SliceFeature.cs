// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>The default implementation for <see cref="ISliceFeature" />.</summary>
public sealed class SliceFeature : ISliceFeature
{
    /// <summary>Gets a <see cref="ISliceFeature" /> with default values for all properties.</summary>
    public static ISliceFeature Default { get; } = new DefaultSliceFeature();

    /// <inheritdoc/>
    public IActivator? Activator { get; }

    /// <inheritdoc/>
    public SliceEncodeOptions? EncodeOptions { get; }

    /// <inheritdoc/>
    public int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum depth when decoding a class recursively.</summary>
    /// <value>The maximum depth. Defaults to <c>100</c>.</value>
    public int MaxDepth { get; }

    /// <summary>Gets the maximum size of a Slice payload segment, in bytes. A Slice payload segment corresponds to the
    /// encoded arguments of an operation, the encoded return values of an operation, or a portion of a stream of
    /// variable-size elements.</summary>
    /// <value>The maximum segment size. Defaults to <c>1</c> MB.</value>
    public int MaxSegmentSize { get; }

    /// <inheritdoc/>
    public Func<ServiceAddress, GenericProxy?, GenericProxy>? ProxyFactory { get; }

    /// <summary>Constructs a Slice feature.</summary>
    /// <param name="activator">The activator.</param>
    /// <param name="encodeOptions">The encode options.</param>
    /// <param name="maxCollectionAllocation">The maximum collection allocation. Use <c>-1</c> to get the default value:
    /// 8 times <paramref name="maxSegmentSize" /> if set, otherwise the value provided by <paramref
    /// name="defaultFeature" />.</param>
    /// <param name="maxDepth">The maximum depth. Use <c>-1</c> to get the default value.</param>
    /// <param name="maxSegmentSize">The maximum segment size. Use <c>-1</c> to get the default value.</param>
    /// <param name="proxyFactory">The proxy factory.</param>
    /// <param name="defaultFeature">A feature that provides default values for all parameters. <see langword="null" />
    /// is equivalent to <see cref="Default" />.</param>
    public SliceFeature(
        IActivator? activator = null,
        SliceEncodeOptions? encodeOptions = null,
        int maxCollectionAllocation = -1,
        int maxDepth = -1,
        int maxSegmentSize = -1,
        Func<ServiceAddress, GenericProxy?, GenericProxy>? proxyFactory = null,
        ISliceFeature? defaultFeature = null)
    {
        defaultFeature ??= Default;

        Activator = activator ?? defaultFeature.Activator;
        EncodeOptions = encodeOptions ?? defaultFeature.EncodeOptions;

        MaxCollectionAllocation = maxCollectionAllocation >= 0 ? maxCollectionAllocation :
            (maxSegmentSize >= 0 ? 8 * maxSegmentSize : defaultFeature.MaxCollectionAllocation);

        MaxDepth = maxDepth >= 0 ? maxDepth : defaultFeature.MaxDepth;

        MaxSegmentSize = maxSegmentSize >= 0 ? maxSegmentSize : defaultFeature.MaxSegmentSize;

        ProxyFactory = proxyFactory ?? defaultFeature.ProxyFactory;
    }

    private class DefaultSliceFeature : ISliceFeature
    {
        public IActivator? Activator => null;

        public SliceEncodeOptions? EncodeOptions => null;

        public int MaxCollectionAllocation => 8 * MaxSegmentSize;

        public int MaxDepth => 100;

        public int MaxSegmentSize => 1024 * 1024;

        public Func<ServiceAddress, GenericProxy?, GenericProxy>? ProxyFactory => null;
    }
}
