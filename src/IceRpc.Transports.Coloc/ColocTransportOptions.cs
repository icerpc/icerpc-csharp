// Copyright (c) ZeroC, Inc.

namespace IceRpc.Transports.Coloc;

/// <summary>A property bag used to configure a <see cref="ColocTransport" />.</summary>
public sealed record class ColocTransportOptions
{
    /// <summary>Gets or sets the maximum length of the pending connections queue.</summary>
    /// <value>The maximum queue length. The value cannot be less than <c>1</c>. Defaults to <c>511</c>.</value>
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
    /// <value>The pause writer threshold. The value cannot be less than <c>1</c> KB. Defaults to <c>64</c> KB.</value>
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
    /// <value>The resume writer threshold. The value cannot be less than <c>1</c> KB and cannot be greater than <see
    /// cref="PauseWriterThreshold"/>. Defaults to <c>32</c> KB.</value>
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
