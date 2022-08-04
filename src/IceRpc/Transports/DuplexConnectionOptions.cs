// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="IDuplexConnection"/>.</summary>
public record class DuplexConnectionOptions
{
    /// <summary>Gets or sets the minimum size of the segment requested from the <see cref="Pool"/>.</summary>
    /// <value>The minimum size of the segment requested from the <see cref="Pool"/>.</value>
    public int MinSegmentSize
    {
        get => _minSegmentSize;
        set => _minSegmentSize = value >= 1024 ? value :
            throw new ArgumentException($"{nameof(MinSegmentSize)} can't be less than 1KB", nameof(value));
    }

    /// <summary>Gets or sets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
    /// <value>A pool of memory blocks used for buffer management.</value>
    public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

    private int _minSegmentSize = 4096;
}
