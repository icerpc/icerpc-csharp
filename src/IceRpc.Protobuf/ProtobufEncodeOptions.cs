// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Protobuf;

/// <summary>Represents a property bag used to configure the encoding of payloads.</summary>
public sealed class ProtobufEncodeOptions
{
    /// <summary>Gets the default instance of <see cref="ProtobufEncodeOptions" />.</summary>
    public static ProtobufEncodeOptions Default { get; } = new();

    /// <summary>Gets the pipe options that the Slice engine uses when creating pipes. The Slice engine creates a pipe
    /// when encoding a request or response payload, and when encoding an async enumerable into a
    /// <see cref="PipeReader" />.</summary>
    public PipeOptions PipeOptions { get; }

    /// <summary>Constructs a new instance.</summary>
    /// <param name="pool">The pool parameter for the constructor of <see cref="System.IO.Pipelines.PipeOptions" />.
    /// </param>
    /// <param name="minimumSegmentSize">The minimum segment size for the constructor of
    /// <see cref="System.IO.Pipelines.PipeOptions" />.</param>
    public ProtobufEncodeOptions(
        MemoryPool<byte>? pool = default,
        int minimumSegmentSize = -1)
    {
        // We keep the default readerScheduler (ThreadPool) because pipes created from these PipeOptions are never
        // ReadAsync concurrently with a FlushAsync/Complete on the pipe writer. The writerScheduler does not matter
        // since FlushAsync never blocks.
        PipeOptions = new(
            pool: pool,
            minimumSegmentSize: minimumSegmentSize,
            pauseWriterThreshold: 0,
            useSynchronizationContext: false);
    }
}
