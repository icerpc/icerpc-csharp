// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="IMultiplexedConnection" />.</summary>
public record class MultiplexedConnectionOptions
{
    /// <summary>Gets or sets the minimum amount of time the multiplexed transport must allow for a connection
    /// establishment handshake to complete.</summary>
    /// <value>The handshake timeout. The default (Zero) means use the underlying implementation default timeout.
    /// </value>
    /// <remarks>The caller is expected to cancel a connection establishment attempt after its own "connect timeout". As
    /// a result, a multiplexed transport implementation may choose to always provide an infinite handshake timeout,
    /// regardless of the value of this option.</remarks>
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets the maximum allowed number of simultaneous remote bidirectional streams that can be
    /// opened.</summary>
    /// <value>The maximum number of remote bidirectional streams. Defaults to <c>100</c>.</value>
    public int MaxBidirectionalStreams { get; set; } = DefaultMaxBidirectionalStreams;

    /// <summary>Gets or sets the maximum allowed number of simultaneous remote unidirectional streams that can be
    /// opened.</summary>
    /// <value>The maximum number of remote unidirectional streams. Defaults to <c>100</c>.</value>
    public int MaxUnidirectionalStreams { get; set; } = DefaultMaxUnidirectionalStreams;

    /// <summary>Gets or sets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
    /// <value>The minimum size in bytes of the segment requested from the <see cref="Pool" />.  Defaults to <c>4</c>
    /// KB.</value>
    public int MinSegmentSize { get; set; } = 4096;

    /// <summary>Gets or sets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
    /// <value>A pool of memory blocks used for buffer management. Defaults to <see cref="MemoryPool{T}.Shared"
    /// />.</value>
    public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

    internal const int DefaultMaxBidirectionalStreams = 100;
    internal const int DefaultMaxUnidirectionalStreams = 100;
}
