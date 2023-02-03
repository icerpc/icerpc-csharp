// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="SlicClientTransport" /> or
/// <see cref="SlicServerTransport" />.</summary>
public sealed record class SlicTransportOptions
{
    /// <summary>Gets or sets the idle timeout. This timeout is used to monitor the transport connection health. If no
    /// data is received within the idle timeout period, the transport connection is aborted. The default is 60s.
    /// </summary>
    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set => _idleTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException(
                $"The value '0' is not a valid for {nameof(IdleTimeout)} property.",
                nameof(value));
    }

    /// <summary>Gets or sets the maximum packet size in bytes.</summary>
    /// <value>The maximum packet size in bytes. It can't be less than 1KB and the default value is
    /// 32KB.</value>
    public int PacketMaxSize
    {
        get => _packetMaxSize;
        set => _packetMaxSize = value >= 1024 ? value :
            throw new ArgumentException($"The {nameof(PacketMaxSize)} value cannot be less than 1KB.", nameof(value));
    }

    /// <summary>Gets or sets the number of bytes when writes on a Slic stream starts blocking.</summary>
    /// <value>The pause writer threshold.</value>
    public int PauseWriterThreshold
    {
        get => _pauseWriterThreshold;
        set => _pauseWriterThreshold = value >= 1024 ? value :
            throw new ArgumentException(
                $"The {nameof(PauseWriterThreshold)} value cannot be less than 1KB.",
                nameof(value));
    }

    /// <summary>Gets or sets the number of bytes when writes on a Slic stream stops blocking.</summary>
    /// <value>The resume writer threshold.</value>
    public int ResumeWriterThreshold
    {
        get => _resumeWriterThreshold;
        set => _resumeWriterThreshold =
            value < 1024 ? throw new ArgumentException(
                $"The {nameof(ResumeWriterThreshold)} value cannot be less than 1KB.", nameof(value)) :
            value > _pauseWriterThreshold ? throw new ArgumentException(
                $"The {nameof(ResumeWriterThreshold)} value cannot be greater than the {nameof(PauseWriterThreshold)} value.",
                nameof(value)) :
            value;
    }

    private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
    // The default packet size matches the SSL record maximum data size to avoid fragmentation of the Slic packet
    // when using SSL.
    private int _packetMaxSize = 16384;
    private int _pauseWriterThreshold = 65536;
    private int _resumeWriterThreshold = 32768;
}
