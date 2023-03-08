// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc;

/// <summary>A property bag used to configure client and server connections.</summary>
public record class ConnectionOptions
{
    /// <summary>Gets or sets the dispatcher that dispatches requests received by this connection.</summary>
    /// <value>The dispatcher that dispatches requests received by this connection, or null if this connection does
    /// not accept requests.</value>
    public IDispatcher? Dispatcher { get; set; }

    /// <summary>Gets or sets a value indicating whether or not a read on the underlying transport connection fails when
    /// this read waits for over <see cref="IceIdleTimeout" /> to receive any byte. This option is specific to the ice
    /// protocol.</summary>
    /// <value>Defaults to <see langword="false"/> for compatibility with the default ACM configuration of Ice 3.6 and
    /// Ice 3.7. The recommended setting is <see langword="true"/> when the peer is configured to send heartbeats all
    /// the time or whenever it does not write to the connection.</value>
    /// <remarks>When setting to <see langword="true"/>, make the sure the peer's idle timeout is equal to or less than
    /// <see cref="IceIdleTimeout" />.</remarks>
    public bool IceIdleCheckEnabled { get; set; }

    /// <summary>Gets or sets the idle timeout. This option is specific to the ice protocol and is used in conjunction
    /// with the <see cref="IceIdleCheckEnabled" /> option. Once the connection is established and regardless of the
    /// value of <see cref="IceIdleCheckEnabled" />, the local runtime sends a heartbeat to the peer when there is no
    /// write on the connection for idle timeout / 2.</summary>
    /// <value>Defaults to 60 seconds to match the default ACM configuration in Ice 3.6 and Ice 3.7.</value>
    public TimeSpan IceIdleTimeout
    {
        get => _iceIdleTimeout;
        set => _iceIdleTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(IceIdleTimeout)}", nameof(value));
    }

    /// <summary>Gets or sets the inactivity timeout. This timeout is used to gracefully shutdown the connection if
    /// it's inactive for longer than this timeout. A connection is considered inactive when there's no invocation or
    /// dispatch in progress.</summary>
    /// <value>Defaults to <c>5</c> minutes.</value>
    public TimeSpan InactivityTimeout
    {
        get => _inactivityTimeout;
        set => _inactivityTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(InactivityTimeout)}", nameof(value));
    }

    /// <summary>Gets or sets the maximum number of requests that a connection can dispatch concurrently. Once this
    /// limit is reached, the connection stops reading new requests off its underlying transport connection.</summary>
    /// <value>The maximum number of requests that a connection can dispatch concurrently. 0 means no maximum. The
    /// default value is 100 requests.</value>
    /// <remarks>With the icerpc protocol, you may also need to set <see cref="MaxIceRpcBidirectionalStreams" /> and
    /// <see cref="MaxIceRpcUnidirectionalStreams" />. A typical two-way dispatch holds onto one bidirectional stream
    /// while a typical oneway dispatch quickly releases its unidirectional stream and then executes without consuming
    /// any stream.</remarks>
    public int MaxDispatches
    {
        get => _maxDispatches;
        set => _maxDispatches = value >= 0 ? value :
            throw new ArgumentOutOfRangeException(nameof(value), "value must be 0 or greater");
    }

    /// <summary>Gets or sets the maximum size of a frame received over the ice protocol.</summary>
    /// <value>The maximum size of an incoming frame, in bytes. This value must be at least 256. The default value
    /// is 1 MB.</value>
    public int MaxIceFrameSize
    {
        get => _maxIceFrameSize;
        set => _maxIceFrameSize = value >= IceMinFrameSize ? value :
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"{nameof(MaxIceFrameSize)} must be at least {IceMinFrameSize}");
    }

    /// <summary>Gets or sets the maximum allowed number of simultaneous remote bidirectional streams that can be
    /// accepted on an icerpc connection. When this limit is reached, the peer is not allowed to open any new
    /// bidirectional stream. Since an bidirectional stream is opened for each two-way invocation, the sending of the
    /// two-way invocation will be delayed until another two-way invocation's stream completes.</summary>
    /// <value>The maximum number of bidirectional streams. It can't be less than 1 and the default value is 100.
    /// </value>
    public int MaxIceRpcBidirectionalStreams
    {
        get => _maxIceRpcBidirectionalStreams;
        set => _maxIceRpcBidirectionalStreams = value > 0 ? value :
            throw new ArgumentException(
                $"{nameof(MaxIceRpcBidirectionalStreams)} can't be less than 1",
                nameof(value));
    }

    /// <summary>Gets or sets the maximum size of icerpc protocol header.</summary>
    /// <value>The maximum size of the header of an incoming request, response or control frame, in bytes. The
    /// default value is 16,383, and the range of this value is 63 to 1,048,575.</value>
    public int MaxIceRpcHeaderSize
    {
        get => _maxIceRpcHeaderSize;
        set => _maxIceRpcHeaderSize = IceRpcCheckMaxHeaderSize(value);
    }

    /// <summary>Gets or sets the maximum allowed number of simultaneous remote unidirectional streams that can be
    /// accepted on an icerpc connection. When this limit is reached, the peer is not allowed to open any new
    /// unidirectional stream. Since an unidirectional stream is opened for each one-way invocation, the sending of the
    /// one-way invocation will be delayed until another one-way invocation's stream completes.</summary>
    /// <value>The maximum number of unidirectional streams. It can't be less than 1 and the default value is 100.
    /// </value>
    public int MaxIceRpcUnidirectionalStreams
    {
        get => _maxIceRpcUnidirectionalStreams;
        set => _maxIceRpcUnidirectionalStreams = value > 0 ? value :
            throw new ArgumentException(
                $"{nameof(MaxIceRpcUnidirectionalStreams)} can't be less than 1",
                nameof(value));
    }

    /// <summary>Gets or sets the minimum size of the segment requested from the <see cref="Pool" />.</summary>
    /// <value>The minimum size of the segment requested from the <see cref="Pool" />.</value>
    public int MinSegmentSize
    {
        get => _minSegmentSize;
        set => _minSegmentSize = value >= 1024 ? value :
            throw new ArgumentException($"{nameof(MinSegmentSize)} can't be less than 1KB", nameof(value));
    }

    /// <summary>Gets or sets the <see cref="MemoryPool{T}" /> object used by the connection for allocating memory
    /// blocks.</summary>
    /// <value>A pool of memory blocks used for buffer management.</value>
    public MemoryPool<byte> Pool { get; set; } = MemoryPool<byte>.Shared;

    /// <summary>The default value for <see cref="MaxIceRpcHeaderSize" />.</summary>
    internal const int DefaultMaxIceRpcHeaderSize = 16_383;

    private const int IceMinFrameSize = 256;

    private TimeSpan _iceIdleTimeout = TimeSpan.FromSeconds(60);
    private TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(5);
    private int _maxDispatches = 100;
    private int _maxIceFrameSize = 1024 * 1024;
    private int _maxIceRpcBidirectionalStreams = MultiplexedConnectionOptions.DefaultMaxBidirectionalStreams;
    private int _maxIceRpcHeaderSize = DefaultMaxIceRpcHeaderSize;
    private int _maxIceRpcUnidirectionalStreams = MultiplexedConnectionOptions.DefaultMaxUnidirectionalStreams;
    private int _minSegmentSize = 4096;

    internal static int IceRpcCheckMaxHeaderSize(long value) => value is >= 63 and <= 1_048_575 ? (int)value :
        throw new ArgumentOutOfRangeException(nameof(value), "value must be between 63 and 1,048,575");
}
