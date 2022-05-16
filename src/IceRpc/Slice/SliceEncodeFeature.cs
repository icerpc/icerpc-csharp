// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>The default implementation for <see cref="ISliceEncodeFeature"/>.</summary>
public sealed class SliceEncodeFeature : ISliceEncodeFeature
{
    /// <summary>Returns the default instance of <see cref="ISliceEncodeFeature"/>.</summary>
    public static SliceEncodeFeature Default { get; } = new();

    /// <inheritdoc/>
    public PipeOptions PipeOptions { get; }

    /// <inheritdoc/>
    public int StreamFlushThreshold { get; }

    /// <summary>Constructs a <see cref="SliceEncodeFeature"/>.</summary>
    /// <param name="pool">The pool parameter for the constructor of <see cref="System.IO.Pipelines.PipeOptions"/>.
    /// </param>
    /// <param name="minimumSegmentSize">The minimum segment size for the constructor of
    /// <see cref="System.IO.Pipelines.PipeOptions"/>.</param>
    /// <param name="streamFlushThreshold">The value of <see cref="StreamFlushThreshold"/>. The default value (-1) is
    /// equivalent to 16 KB.</param>
    public SliceEncodeFeature(
        MemoryPool<byte>? pool = default,
        int minimumSegmentSize = -1,
        int streamFlushThreshold = -1)
    {
        PipeOptions = new(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            readerScheduler: PipeScheduler.Inline,
            pauseWriterThreshold: 0,
            useSynchronizationContext: false);

        StreamFlushThreshold = streamFlushThreshold == -1 ? 16 * 1024 : streamFlushThreshold;
    }
}
