// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="SlicClientTransport"/> or
/// <see cref="SlicServerTransport"/>.</summary>
public sealed record class SlicTransportOptions
{
    /// <summary>Gets or sets the bidirectional stream maximum count to limit the number of concurrent
    /// bidirectional streams opened on a connection. When this limit is reached, trying to open a new
    /// bidirectional stream will be delayed until a bidirectional stream is closed. Since an
    /// bidirectional stream is opened for each two-way invocation, the sending of the two-way
    /// invocation will be delayed until another two-way invocation on the connection completes.</summary>
    /// <value>The bidirectional stream maximum count. It can't be less than 1 and the default value is 100.</value>
    public int BidirectionalStreamMaxCount
    {
        get => _bidirectionalStreamMaxCount;
        set => _bidirectionalStreamMaxCount = value > 0 ? value :
            throw new ArgumentException(
                $"{nameof(BidirectionalStreamMaxCount)} can't be less than 1",
                nameof(value));
    }

    /// <summary>Gets or sets the idle timeout. This timeout is used to monitor the transport connection health. If no
    /// data is received within the idle timeout period, the transport connection is aborted. The default is 60s.
    /// </summary>
    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set => _idleTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(IdleTimeout)}", nameof(value));
    }

    /// <summary>Gets or sets the <see cref="MemoryPool{T}" /> object used for buffer management.</summary>
    /// <value>A pool of memory blocks used for buffer management.</value>
    public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

    /// <summary>Gets or sets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
    /// <value>The minimum size of the segment requested from the <see cref="Pool" />.</value>
    public int MinimumSegmentSize
    {
        get => _minimumSegmentSize;
        set => _minimumSegmentSize = value >= 1024 ? value :
            throw new ArgumentException($"{nameof(MinimumSegmentSize)} can't be less than 1KB", nameof(value));
    }

    /// <summary>Gets or sets the maximum packet size in bytes.</summary>
    /// <value>The maximum packet size in bytes. It can't be less than 1KB and the default value is
    /// 32KB.</value>
    public int PacketMaxSize
    {
        get => _packetMaxSize;
        set => _packetMaxSize = value >= 1024 ? value :
            throw new ArgumentException($"{nameof(PacketMaxSize)} can't be less than 1KB", nameof(value));
    }

    /// <summary>Gets or sets the number of bytes when writes on a Slic stream starts blocking.</summary>
    /// <value>The pause writer threshold.</value>
    public int PauseWriterThreshold
    {
        get => _pauseWriterThreshold;
        set => _pauseWriterThreshold = value >= 1024 ? value :
            throw new ArgumentException($"{nameof(PauseWriterThreshold)} can't be less than 1KB", nameof(value));
    }

    /// <summary>Gets or sets the number of bytes when writes on a Slic stream stops blocking.</summary>
    /// <value>The resume writer threshold.</value>
    public int ResumeWriterThreshold
    {
        get => _resumeWriterThreshold;
        set => _resumeWriterThreshold =
            value < 1024 ? throw new ArgumentException(
                @$"{nameof(ResumeWriterThreshold)} can't be less than 1KB", nameof(value)) :
            value > _pauseWriterThreshold ? throw new ArgumentException(
                @$"{nameof(ResumeWriterThreshold)
                    } can't be greater can't be greater than {nameof(PauseWriterThreshold)}",
                nameof(value)) :
            value;
    }

    /// <summary>Gets or sets the unidirectional stream maximum count to limit the number of concurrent
    /// unidirectional streams opened on a connection. When this limit is reached, trying to open a new
    /// unidirectional stream will be delayed until an unidirectional stream is closed. Since an unidirectional
    /// stream is opened for each one-way invocation, the sending of the one-way invocation will be delayed
    /// until another one-way invocation on the connection completes.</summary>
    /// <value>The unidirectional stream maximum count. It can't be less than 1 and the default value is
    /// 100.</value>
    public int UnidirectionalStreamMaxCount
    {
        get => _unidirectionalStreamMaxCount;
        set => _unidirectionalStreamMaxCount = value > 0 ? value :
            throw new ArgumentException(
                $"{nameof(UnidirectionalStreamMaxCount)} can't be less than 1",
                nameof(value));
    }

    private int _bidirectionalStreamMaxCount = 100;
    private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
    private int _minimumSegmentSize = 4096;
    // The default packet size matches the SSL record maximum data size to avoid fragmentation of the Slic packet
    // when using SSL.
    private int _packetMaxSize = 16384;
    private int _pauseWriterThreshold = 65536;
    private int _resumeWriterThreshold = 32768;
    private int _unidirectionalStreamMaxCount = 100;
}
