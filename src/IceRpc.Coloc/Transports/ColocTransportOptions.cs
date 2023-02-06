// Copyright (c) ZeroC, Inc.

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="ColocTransport" />.</summary>
public sealed record class ColocTransportOptions
{
    /// <summary>Gets or sets the maximum length of the pending connections queue.</summary>
    /// <value>The maximum queue length, the default value is 511. The value cannot be less than 1.</value>
    public int ListenBacklog
    {
        get => _listenBacklog;
        set => _listenBacklog = value >= 1 ? value :
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Invalid value '{value}' for {nameof(ListenBacklog)}, it cannot be less than 1.");
    }

    /// <summary>Gets or sets the number of bytes in the Coloc connection when <see cref="IDuplexConnection.WriteAsync"
    /// /> starts blocking.</summary>
    /// <value>The pause writer threshold, the default value is 64KB. The value cannot be less than 1KB.</value>
    public int PauseWriterThreshold
    {
        get => _pauseWriterThreshold;
        set => _pauseWriterThreshold = value >= 1024 ? value :
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Invalid value '{value}' for {nameof(PauseWriterThreshold)}, it cannot be less than 1KB.");
    }

    /// <summary>Gets or sets the number of bytes in the Coloc connection when <see cref="IDuplexConnection.WriteAsync"
    /// /> stops blocking.</summary>
    /// <value>The resume writer threshold, the default value is 32KB. The value cannot be less than 1KB and cannot be
    /// greater than the <see cref="PauseWriterThreshold"/> value.</value>
    public int ResumeWriterThreshold
    {
        get => _resumeWriterThreshold;
        set => _resumeWriterThreshold =
            value < 1024 ? throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Invalid value '{value}' for {nameof(ResumeWriterThreshold)}, it cannot be less than 1KB.") :
            value > _pauseWriterThreshold ? throw new ArgumentException(
                $"The value of {nameof(ResumeWriterThreshold)} cannot be greater than the value of {nameof(PauseWriterThreshold)}.",
                nameof(value)) :
            value;
    }

    private int _listenBacklog = 511;
    private int _pauseWriterThreshold = 65536;
    private int _resumeWriterThreshold = 32768;
}
