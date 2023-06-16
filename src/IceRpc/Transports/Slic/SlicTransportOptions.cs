// Copyright (c) ZeroC, Inc.

namespace IceRpc.Transports.Slic;

/// <summary>A property bag used to configure a <see cref="SlicClientTransport" /> or
/// <see cref="SlicServerTransport" />.</summary>
public sealed record class SlicTransportOptions
{
    /// <summary>Gets or sets the idle timeout. This timeout is used to monitor the transport connection health. If no
    /// data is received within the idle timeout period, the transport connection is aborted.
    /// </summary>
    /// <value>The idle timeout. Defaults to <c>30</c> s.</value>
    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set => _idleTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException(
                $"The value '0' is not a valid for {nameof(IdleTimeout)} property.",
                nameof(value));
    }

    /// <summary>Gets or sets the maximum packet size in bytes.</summary>
    /// <value>The maximum packet size in bytes. It can't be less than <c>1</c> KB. Defaults to <c>32</c> KB.</value>
    public int PacketMaxSize
    {
        get => _packetMaxSize;
        set => _packetMaxSize = value >= 1024 ? value :
            throw new ArgumentException($"The {nameof(PacketMaxSize)} value cannot be less than 1KB.", nameof(value));
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
            value > MaxWindowSize ?
            throw new ArgumentException(
                $"The {nameof(InitialStreamWindowSize)} value cannot be larger than {MaxWindowSize}.",
                nameof(value)) :
            value;
    }

    // We use the HTTP/2 maximum window size (2GB).
    internal const int MaxWindowSize = int.MaxValue;

    private TimeSpan _idleTimeout = TimeSpan.FromSeconds(30);
    private int _packetMaxSize = 32768;
    // The default specified in the HTTP/2 specification.
    private int _initialStreamWindowSize = 65_536;
}
