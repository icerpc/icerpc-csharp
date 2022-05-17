// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>A feature to customize the encoding of request and response payloads.</summary>
public interface ISliceEncodeFeature
{
    /// <summary>Gets the pipe options that the Slice engine uses when creating pipes. The Slice engine creates a pipe
    /// when encoding a request or response payload, and when encoding an async enumerable into a
    /// <see cref="PipeReader"/>.</summary>
    PipeOptions PipeOptions { get; }

    /// <summary>Gets the stream flush threshold. When encoding a Slice stream (async enumerable), the Slice engine
    /// encodes the values provided by the source async enumerable into a pipe writer. The Slice engine flushes this
    /// pipe writer when no new value is available synchronously, or when it has written StreamFlushThreshold bytes to
    /// this pipe writer.</summary>
    int StreamFlushThreshold { get; }
}
