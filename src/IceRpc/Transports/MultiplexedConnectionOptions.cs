// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="IMultiplexedConnection"/>.</summary>
public record class MultiplexedConnectionOptions
{
    /// <summary>Gets or sets the bidirectional stream maximum count to limit the number of concurrent
    /// bidirectional streams opened on a connection. When this limit is reached, trying to open a new
    /// bidirectional stream will be delayed until a bidirectional stream is closed. Since an
    /// bidirectional stream is opened for each two-way invocation, the sending of the two-way
    /// invocation will be delayed until another two-way invocation on the connection completes.</summary>
    /// <value>The bidirectional stream maximum count. It can't be less than 1 and the default value is 100.</value>
    public int MaxBidirectionalStreams { get; set; } = DefaultMaxBidirectionalStreams;

    /// <summary>Gets or sets the unidirectional stream maximum count to limit the number of concurrent
    /// unidirectional streams opened on a connection. When this limit is reached, trying to open a new
    /// unidirectional stream will be delayed until an unidirectional stream is closed. Since an unidirectional
    /// stream is opened for each one-way invocation, the sending of the one-way invocation will be delayed
    /// until another one-way invocation on the connection completes.</summary>
    /// <value>The unidirectional stream maximum count. It can't be less than 1 and the default value is
    /// 100.</value>
    public int MaxUnidirectionalStreams { get; set; } = DefaultMaxUnidirectionalStreams;

    /// <summary>Gets or sets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
    /// <value>The minimum size of the segment requested from the <see cref="Pool" />.</value>
    public int MinimumSegmentSize { get; set; } = 4096;

    /// <summary>Gets or sets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
    /// <value>A pool of memory blocks used for buffer management.</value>
    public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

    /// <summary>Gets or sets the <see cref="IMultiplexedStreamErrorCodeConverter"/>.</summary>
    /// <value>The <see cref="IMultiplexedStreamErrorCodeConverter"/>.</value>
    public IMultiplexedStreamErrorCodeConverter? StreamErrorCodeConverter { get; set; }

    internal const int DefaultMaxBidirectionalStreams = 100;
    internal const int DefaultMaxUnidirectionalStreams = 100;
}
