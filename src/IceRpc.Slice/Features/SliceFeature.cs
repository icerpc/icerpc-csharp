// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;

namespace IceRpc.Features;

/// <summary>Default implementation of <see cref="ISliceFeature" />.</summary>
public sealed class SliceFeature : ISliceFeature
{
    /// <summary>Gets a <see cref="ISliceFeature" /> with default values for all properties.</summary>
    public static ISliceFeature Default { get; } = new DefaultSliceFeature();

    /// <inheritdoc/>
    public ISliceProxy? BaseProxy { get; }

    /// <inheritdoc/>
    public SliceEncodeOptions? EncodeOptions { get; }

    /// <inheritdoc/>
    public int MaxCollectionAllocation { get; }

    /// <summary>Gets the maximum size of a Slice payload segment, in bytes. A Slice payload segment corresponds to the
    /// encoded arguments of an operation, the encoded return values of an operation, or a portion of a stream of
    /// variable-size elements.</summary>
    /// <value>The maximum segment size. Defaults to <c>1</c> MB.</value>
    public int MaxSegmentSize { get; }

    /// <summary>Constructs a Slice feature.</summary>
    /// <param name="encodeOptions">The encode options.</param>
    /// <param name="maxCollectionAllocation">The maximum collection allocation. Use <c>-1</c> to get the default value:
    /// 8 times <paramref name="maxSegmentSize" /> if set, otherwise the value provided by <paramref
    /// name="defaultFeature" />.</param>
    /// <param name="maxSegmentSize">The maximum segment size. Use <c>-1</c> to get the default value.</param>
    /// <param name="baseProxy">The base proxy, used when decoding service addresses into proxies.</param>
    /// <param name="defaultFeature">A feature that provides default values for all parameters. <see langword="null" />
    /// is equivalent to <see cref="Default" />.</param>
    public SliceFeature(
        SliceEncodeOptions? encodeOptions = null,
        int maxCollectionAllocation = -1,
        int maxSegmentSize = -1,
        ISliceProxy? baseProxy = null,
        ISliceFeature? defaultFeature = null)
    {
        defaultFeature ??= Default;

        EncodeOptions = encodeOptions ?? defaultFeature.EncodeOptions;

        MaxCollectionAllocation = maxCollectionAllocation >= 0 ? maxCollectionAllocation :
            (maxSegmentSize >= 0 ? 8 * maxSegmentSize : defaultFeature.MaxCollectionAllocation);

        MaxSegmentSize = maxSegmentSize >= 0 ? maxSegmentSize : defaultFeature.MaxSegmentSize;

        BaseProxy = baseProxy ?? defaultFeature.BaseProxy;
    }

    private class DefaultSliceFeature : ISliceFeature
    {
        public ISliceProxy? BaseProxy => null;

        public SliceEncodeOptions? EncodeOptions => null;

        public int MaxCollectionAllocation => 8 * MaxSegmentSize;

        public int MaxSegmentSize => 1024 * 1024;
    }
}
