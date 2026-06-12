// Copyright (c) ZeroC, Inc.

namespace IceRpc.Transports.Slic;

/// <summary>A property bag used to configure a <see cref="SlicClientTransport" /> or
/// <see cref="SlicServerTransport" />.</summary>
public sealed record class SlicTransportOptions
{
    /// <summary>Gets or sets the idle timeout. This timeout is used to monitor the transport connection health. If no
    /// data is received within the idle timeout period, the transport connection is aborted. The effective idle timeout
    /// is negotiated with the peer: use <see cref="Timeout.InfiniteTimeSpan" /> to defer to the peer's idle timeout.
    /// Idle timeout monitoring is disabled only when both sides use <see cref="Timeout.InfiniteTimeSpan" />.</summary>
    /// <value>The idle timeout. It must be positive or <see cref="Timeout.InfiniteTimeSpan" />.
    /// Defaults to <c>30</c> s.</value>
    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set
        {
            if (value != Timeout.InfiniteTimeSpan && value <= TimeSpan.Zero)
            {
                throw new ArgumentException(
                    $"The {nameof(IdleTimeout)} value must be positive or Timeout.InfiniteTimeSpan.",
                    nameof(value));
            }
            _idleTimeout = value;
        }
    }

    /// <summary>Gets or sets the initial stream window size. It defines the initial size of the stream receive buffer
    /// for data that has not been consumed yet by the application. When this buffer is full the sender should stop
    /// sending additional data.</summary>
    /// <value>The initial window size in bytes. It can't be less than <c>1</c> KB. Defaults to <c>64</c> KB.</value>
    public int InitialStreamWindowSize
    {
        get => _initialStreamWindowSize;
        set => _initialStreamWindowSize =
            value < 1024 ?
            throw new ArgumentException(
                $"The {nameof(InitialStreamWindowSize)} value cannot be less than 1 KB.",
                nameof(value)) :
            value;
    }

    /// <summary>Gets or sets the maximum number of Pong frames that can be queued for sending. A Pong frame is queued
    /// for sending when a Ping frame is received, and remains queued until it is written to the underlying duplex
    /// connection. The connection is aborted when a Ping frame is received while this limit is reached; this protects
    /// against a peer that sends Ping frames faster than the connection can write the Pong frames.</summary>
    /// <value>The maximum number of Pong frames queued for sending. It can't be less than <c>1</c>. Defaults to
    /// <c>100</c>.</value>
    public int MaxOutstandingPongs
    {
        get => _maxOutstandingPongs;
        set => _maxOutstandingPongs =
            value < 1 ?
            throw new ArgumentException(
                $"The {nameof(MaxOutstandingPongs)} value cannot be less than 1.",
                nameof(value)) :
            value;
    }

    /// <summary>Gets or sets the maximum stream frame size in bytes.</summary>
    /// <value>The maximum stream frame size in bytes. It can't be less than <c>1024</c> or greater than
    /// <c>16,777,215</c> (2^24 - 1). Defaults to <c>32,768</c>.</value>
    public int MaxStreamFrameSize
    {
        get => _maxStreamFrameSize;
        set => _maxStreamFrameSize =
            value < 1024 ?
            throw new ArgumentException(
                $"The {nameof(MaxStreamFrameSize)} value cannot be less than 1 KB.",
                nameof(value)) :
            value > MaxStreamFrameSizeCeiling ?
            throw new ArgumentException(
                $"The {nameof(MaxStreamFrameSize)} value cannot be larger than {MaxStreamFrameSizeCeiling}.",
                nameof(value)) :
            value;
    }

    /// <summary>Gets or sets the per-connection pause writer threshold. It defines the maximum amount of data that
    /// Slic buffers locally on the connection's outbound pipe before
    /// <see cref="System.IO.Pipelines.PipeWriter.FlushAsync" /> on the underlying pipe starts blocking. This bounds the
    /// memory used by the connection's outbound buffering when a peer is slow or not reading.</summary>
    /// <value>The pause writer threshold in bytes. Set to <c>0</c> to disable this flow control mechanism. Otherwise,
    /// it can't be less than <c>1</c> KB. Defaults to <c>64</c> KB (the <see cref="System.IO.Pipelines.Pipe" />
    /// default).</value>
    // The default specified by System.IO.Pipelines.PipeOptions.
    public int PauseWriterThreshold { get; set => field = ValidatePauseWriterThreshold(value); } = 65_536;

    // Upper bound on MaxStreamFrameSize (local and peer-advertised). Matches HTTP/2's SETTINGS_MAX_FRAME_SIZE
    // ceiling. Larger frames worsen head-of-line blocking across multiplexed streams without improving
    // throughput at any realistic link speed.
    internal const int MaxStreamFrameSizeCeiling = 16_777_215;

    private static int ValidatePauseWriterThreshold(int value) =>
        value < 0 ?
            throw new ArgumentException(
                $"The {nameof(PauseWriterThreshold)} value cannot be negative.",
                nameof(value)) :
            value > 0 && value < 1024 ?
            throw new ArgumentException(
                $"The {nameof(PauseWriterThreshold)} value cannot be less than 1 KB unless it is 0.",
                nameof(value)) :
            value;

    // We use the HTTP/2 maximum window size (2GB).
    internal const int MaxWindowSize = int.MaxValue;

    private TimeSpan _idleTimeout = TimeSpan.FromSeconds(30);

    // The default specified in the HTTP/2 specification.
    private int _initialStreamWindowSize = 65_536;
    private int _maxOutstandingPongs = 100;
    private int _maxStreamFrameSize = 32_768;
}
