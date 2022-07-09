// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>The default implementation for <see cref="ISliceFeature"/>.</summary>
public sealed class SliceDecodeFeature : ISliceFeature
{
    /// <summary>Gets the default instance of <see cref="SliceDecodeFeature"/>.</summary>
    public static SliceDecodeFeature Default { get; } = new();

    /// <inheritdoc/>
    public IActivator? Activator { get; init; }

    /// <inheritdoc/>
    public SliceEncodeOptions? EncodeOptions { get; init; }

    /// <summary>Gets or initializes the maximum collection allocation when decoding a payload, in bytes.</summary>
    /// <value>A value greater than or equal to 0. The default is 8 times <see cref="MaxSegmentSize"/>.</value>
    public int MaxCollectionAllocation
    {
        get => _maxCollectionAllocation ?? 8 * MaxSegmentSize;
        init => _maxCollectionAllocation = value;
    }

    /// <summary>Gets or initializes the maximum depth when decoding a type recursively.</summary>
    /// <value>The default value is 100.</value>
    public int MaxDepth { get; init; } = 100;

    /// <summary>Gets or initializes the maximum size of a Slice payload segment, in bytes. A Slice payload segment
    /// corresponds to the encoded arguments of an operation, the encoded return values of an operation, or a portion
    /// of a stream of variable-size elements.</summary>
    /// <value>The default value is 1 MB.</value>
    public int MaxSegmentSize { get; init; } = 1024 * 1024;

    /// <inheritdoc/>
    public ServiceProxyFactory? ServiceProxyFactory { get; init; }

    /// <summary>Gets or initializes the stream pause writer threshold. When the Slice engine decodes a stream into an
    /// async enumerable, it will pause when the number of bytes decoded but not read is greater or equal to this value.
    /// </summary>
    /// <value>A value of <c>0</c> means no threshold. The default is 64 KB.</value>
    public int StreamPauseWriterThreshold { get; init; } = 64 * 1024;

    /// <summary>Gets or initializes the stream resume writer threshold. When the decoding of a stream into an async
    /// enumerable is paused (<see cref="StreamPauseWriterThreshold"/>), the decoding resumes when the number of bytes
    /// decoded but not read falls below this threshold.</summary>
    /// <value>The default value is half of <see cref="StreamPauseWriterThreshold"/>.</value>
    public int StreamResumeWriterThreshold
    {
        get => _streamResumeWriterThreshold ?? StreamPauseWriterThreshold / 2;
        init => _streamResumeWriterThreshold = value;
    }

    private int? _maxCollectionAllocation;
    private int? _streamResumeWriterThreshold;
}
