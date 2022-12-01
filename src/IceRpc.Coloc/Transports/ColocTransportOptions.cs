// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>A property bag used to configure a <see cref="ColocTransport" />.</summary>
public sealed record class ColocTransportOptions
{
    /// <summary>Gets or sets the maximum length of the pending connections queue.</summary>
    /// <value>The maximum queue length.</value>
    public int ListenBacklog
    {
        get => _listenBacklog;
        set => _listenBacklog = value >= 1 ? value :
            throw new ArgumentException($"{nameof(ListenBacklog)} can't be less than 1", nameof(value));
    }

    /// <summary>Gets or sets the number of bytes in the Coloc connection when <see cref="IDuplexConnection.WriteAsync"
    /// /> starts blocking.</summary>
    /// <value>The pause writer threshold.</value>
    public int PauseWriterThreshold
    {
        get => _pauseWriterThreshold;
        set => _pauseWriterThreshold = value >= 1024 ? value :
            throw new ArgumentException($"{nameof(PauseWriterThreshold)} can't be less than 1KB", nameof(value));
    }

    /// <summary>Gets or sets the number of bytes in the Coloc connection when <see cref="IDuplexConnection.WriteAsync"
    /// /> stops blocking.</summary>
    /// <value>The resume writer threshold.</value>
    public int ResumeWriterThreshold
    {
        get => _resumeWriterThreshold;
        set => _resumeWriterThreshold =
            value < 1024 ? throw new ArgumentException(
                $"{nameof(ResumeWriterThreshold)} can't be less than 1KB", nameof(value)) :
            value > _pauseWriterThreshold ? throw new ArgumentException(
                $"{nameof(ResumeWriterThreshold)} can't be greater than {nameof(PauseWriterThreshold)}",
                nameof(value)) :
            value;
    }

    private int _listenBacklog = 511;
    private int _pauseWriterThreshold = 65536;
    private int _resumeWriterThreshold = 32768;
}
