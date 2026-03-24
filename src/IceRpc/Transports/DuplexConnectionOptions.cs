// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="IDuplexConnection" />.</summary>
public record class DuplexConnectionOptions
{
    /// <summary>Gets or sets the application protocol for ALPN (Application-Layer Protocol Negotiation).</summary>
    /// <value>The application protocol name, or <see langword="null" /> if no application protocol is configured.
    /// Defaults to <see langword="null" />.</value>
    public string? ApplicationProtocol { get; set; }

    /// <summary>Gets or sets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
    /// <value>The minimum size in bytes of the segment requested from the <see cref="Pool" />. It cannot be less than
    /// <c>1</c> KB. Defaults to <c>4</c> KB.</value>
    public int MinSegmentSize
    {
        get => _minSegmentSize;
        set => _minSegmentSize = value >= 1024 ? value :
            throw new ArgumentException($"The {nameof(MinSegmentSize)} argument cannot be less than 1KB.", nameof(value));
    }

    /// <summary>Gets or sets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
    /// <value>A pool of memory blocks used for buffer management. Defaults to <see cref="MemoryPool{T}.Shared"
    /// />.</value>
    public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

    private int _minSegmentSize = 4096;
}
