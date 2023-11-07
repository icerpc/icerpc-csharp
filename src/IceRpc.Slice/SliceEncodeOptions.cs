// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Represents a property bag used to configure the encoding of payloads.</summary>
public sealed class SliceEncodeOptions
{
    /// <summary>Gets the default instance of <see cref="SliceEncodeOptions" />.</summary>
    public static SliceEncodeOptions Default { get; } = new();

    /// <summary>Gets the pipe options that the IceRPC + Slice integration uses when creating pipes. The IceRPC + Slice
    /// integration creates a pipe when encoding a request or response payload, and when encoding an async enumerable
    /// into a <see cref="PipeReader" />.</summary>
    public PipeOptions PipeOptions { get; }

    /// <summary>Gets the stream flush threshold. When encoding a Slice stream (async enumerable), the IceRPC + Slice
    /// integration encodes the values provided by the source async enumerable into a pipe writer. The IceRPC + Slice
    /// integration flushes this pipe writer when no new value is available synchronously, or when it has written
    /// <c>StreamFlushThreshold</c> bytes to this pipe writer.</summary>
    public int StreamFlushThreshold { get; }

    /// <summary>Constructs a new instance.</summary>
    /// <param name="pool">The pool parameter for the constructor of <see cref="System.IO.Pipelines.PipeOptions" />.
    /// </param>
    /// <param name="minimumSegmentSize">The minimum segment size for the constructor of
    /// <see cref="System.IO.Pipelines.PipeOptions" />.</param>
    /// <param name="streamFlushThreshold">The value of <see cref="StreamFlushThreshold" />. The default value (-1) is
    /// equivalent to 16 KB.</param>
    public SliceEncodeOptions(
        MemoryPool<byte>? pool = default,
        int minimumSegmentSize = -1,
        int streamFlushThreshold = -1)
    {
        // We keep the default readerScheduler (ThreadPool) because pipes created from these PipeOptions are never
        // ReadAsync concurrently with a FlushAsync/Complete on the pipe writer. The writerScheduler does not matter
        // since FlushAsync never blocks.
        PipeOptions = new(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            useSynchronizationContext: false);

        StreamFlushThreshold = streamFlushThreshold == -1 ? 16 * 1024 : streamFlushThreshold;
    }
}
