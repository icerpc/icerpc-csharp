// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Configure;

/// <summary>An option class to customize the encoding of a Slice-encoded request or response payloads.</summary>
public sealed record class SliceEncodeOptions
{
    /// <summary>Gets or sets the memory pool to use when encoding payloads.</summary>
    public MemoryPool<byte> MemoryPool { get; set; } = MemoryPool<byte>.Shared;
}
