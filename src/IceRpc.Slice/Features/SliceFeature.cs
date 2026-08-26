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
    /// <value>The maximum collection allocation. Defaults to 8 times <see cref="MaxSegmentSize" />.</value>
    public int MaxCollectionAllocation { get; }

    /// <inheritdoc/>
    /// <value>The maximum size of a Slice payload segment, in bytes. Defaults to <c>1</c> MB.</value>
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
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxCollectionAllocation" /> is a
    /// negative value other than <c>-1</c>, when <paramref name="maxSegmentSize" /> is <c>0</c> or a negative value
    /// other than <c>-1</c>, or when <paramref name="maxSegmentSize" /> is greater than <c>int.MaxValue / 8</c>
    /// (256 MB) while <paramref name="maxCollectionAllocation" /> is <c>-1</c>.</exception>
    public SliceFeature(
        SliceEncodeOptions? encodeOptions = null,
        int maxCollectionAllocation = -1,
        int maxSegmentSize = -1,
        ISliceProxy? baseProxy = null,
        ISliceFeature? defaultFeature = null)
    {
        if (maxCollectionAllocation < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCollectionAllocation),
                $"The value of {nameof(maxCollectionAllocation)} must be greater than or equal to 0, or -1.");
        }
        if (maxSegmentSize is < -1 or 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSegmentSize),
                $"The value of {nameof(maxSegmentSize)} must be greater than 0, or -1.");
        }
        if (maxCollectionAllocation == -1 && maxSegmentSize > int.MaxValue / 8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSegmentSize),
                $"The value of {nameof(maxSegmentSize)} must be less than or equal to int.MaxValue / 8 when {nameof(maxCollectionAllocation)} is -1.");
        }

        defaultFeature ??= Default;

        EncodeOptions = encodeOptions ?? defaultFeature.EncodeOptions;

        MaxCollectionAllocation = maxCollectionAllocation >= 0 ? maxCollectionAllocation :
            (maxSegmentSize > 0 ? 8 * maxSegmentSize : defaultFeature.MaxCollectionAllocation);

        MaxSegmentSize = maxSegmentSize > 0 ? maxSegmentSize : defaultFeature.MaxSegmentSize;

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
