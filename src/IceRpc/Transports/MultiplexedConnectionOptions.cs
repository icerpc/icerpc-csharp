// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="IMultiplexedConnection" />.</summary>
public record class MultiplexedConnectionOptions
{
    /// <summary>Gets or sets the maximum allowed number of simultaneous remote bidirectional streams that can be
    /// opened.</summary>
    /// <value>The maximum number of remote bidirectional streams.</value>
    public int MaxBidirectionalStreams { get; set; } = DefaultMaxBidirectionalStreams;

    /// <summary>Gets or sets the maximum allowed number of simultaneous remote unidirectional streams that can be
    /// opened.</summary>
    /// <value>The maximum number of remote unidirectional streams.</value>
    public int MaxUnidirectionalStreams { get; set; } = DefaultMaxUnidirectionalStreams;

    /// <summary>Gets or sets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
    /// <value>The minimum size of the segment requested from the <see cref="Pool" />.</value>
    public int MinSegmentSize { get; set; } = 4096;

    /// <summary>Gets or sets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
    /// <value>A pool of memory blocks used for buffer management.</value>
    public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

    /// <summary>Gets or sets the <see cref="IMultiplexedStreamErrorCodeConverter" />.</summary>
    /// <value>The <see cref="IMultiplexedStreamErrorCodeConverter" />.</value>
    public IMultiplexedStreamErrorCodeConverter? StreamErrorCodeConverter { get; set; }

    internal const int DefaultMaxBidirectionalStreams = 100;
    internal const int DefaultMaxUnidirectionalStreams = 100;
}
