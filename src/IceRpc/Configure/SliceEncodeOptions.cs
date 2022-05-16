// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Configure;

/// <summary>An option class to customize the encoding of request and response payloads.</summary>
public sealed record class SliceEncodeOptions
{
    /// <summary>Returns the default value for <see cref="PipeOptions"/>.</summary>
    public static PipeOptions DefaultPipeOptions { get; } = new(
        readerScheduler: PipeScheduler.Inline,
        pauseWriterThreshold: 0,
        useSynchronizationContext: false);

    /// <summary>Gets or sets the pipe options that the Slice engine uses when creating pipes. The Slice engine creates
    /// a pipe when encoding a request or response payload, and when encoding an async enumerable into a
    /// <see cref="PipeReader"/>. We suggest you set these pipe options as follows:
    /// - pool: the memory pool of your choice, or keep the default
    /// - readerScheduler: PipeScheduler.Inline
    /// - writerScheduler: keep the default (PipeScheduler.ThreadPool)
    /// - pauseWriterThreshold: 0
    /// - resumeWriterThreshold: keep the default
    /// - minimumSegmentSize: keep the default or customize together with the memory pool
    /// - useSynchronizationContext: false
    /// </summary>
    public PipeOptions PipeOptions { get; set; } = DefaultPipeOptions;

    /// <summary>When encoding a Slice stream (async enumerable), the Slice engine encodes the values provided by the
    /// source async enumerable into a pipe writer and only flushes when no new value is available synchronously or it
    /// has written some number of bytes to this pipe writer.</summary>
    /// <value>The maximum number of bytes encoded synchronously in a stream without flushing the pipe writer. The
    /// default value is 16 KB.</value>
    public int StreamFlushThreshold { get; set; } = 16 * 1024;

    /// <summary>A default instance of <see cref="SliceEncodeOptions"/>.</summary>
    internal static SliceEncodeOptions Default { get; } = new();
}
