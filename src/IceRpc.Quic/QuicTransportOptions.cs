// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;

namespace IceRpc.Transports;

/// <summary>The base options class for TCP transports.</summary>
public record class QuicTransportOptions
{
    /// <summary>Gets or sets the idle timeout. This timeout is used to monitor the transport connection health. If no
    /// data is received within the idle timeout period, the transport connection is aborted. The default is 60s.
    /// </summary>
    public TimeSpan IdleTimeout
    {
        get => _idleTimeout;
        set => _idleTimeout = value != TimeSpan.Zero ? value :
            throw new ArgumentException($"0 is not a valid value for {nameof(IdleTimeout)}", nameof(value));
    }

    /// <summary>Gets or sets the number of bytes when reads from a Quic stream stop.</summary>
    /// <value>The pause reader threshold.</value>
    public int PauseReaderThreshold
    {
        get => _pauseReaderThreshold;
        set => _pauseReaderThreshold = value >= 1024 ? value :
            throw new ArgumentException($"{nameof(PauseReaderThreshold)} can't be less than 1KB", nameof(value));
    }

    /// <summary>Gets or sets the number of bytes when reads on a Quic stream resume.</summary>
    /// <value>The resume reader threshold.</value>
    public int ResumeReaderThreshold
    {
        get => _resumeReaderThreshold;
        set => _resumeReaderThreshold =
            value < 1024 ? throw new ArgumentException(
                $"{nameof(ResumeReaderThreshold)} can't be less than 1KB", nameof(value)) :
            value > _pauseReaderThreshold ? throw new ArgumentException(
                $"{nameof(ResumeReaderThreshold)} can't be greater than {nameof(PauseReaderThreshold)}",
                nameof(value)) :
            value;
    }

    private TimeSpan _idleTimeout = TimeSpan.FromSeconds(60);
    private int _pauseReaderThreshold = 65536;
    private int _resumeReaderThreshold = 32768;
}

/// <summary>The options class for configuring <see cref="QuicClientTransport"/>.</summary>
public sealed record class QuicClientTransportOptions : QuicTransportOptions
{
    /// <summary>Gets or sets the address and port represented by a .NET IPEndPoint to use for a client Quic connection.
    /// If specified the client Quic connection will bind to this address and port before connection establishment.
    /// </summary>
    /// <value>The address and port to bind the socket to.</value>
    public IPEndPoint? LocalNetworkAddress { get; set; }
}

/// <summary>The options class for configuring <see cref="QuicServerTransport"/>.</summary>
public sealed record class QuicServerTransportOptions : QuicTransportOptions
{
    /// <summary>Gets or sets the length of the server socket queue for accepting new connections. If a new connection
    /// request arrives and the queue is full, the client connection establishment will fail with a <see
    /// cref="TransportException"/> and the <see cref="TransportErrorCode.ConnectionRefused"/> error code.</summary>
    /// <value>The server socket backlog size. The default is 511.</value>
    public int ListenerBackLog
    {
        get => _listenerBackLog;
        set => _listenerBackLog = value > 0 ? value :
            throw new ArgumentException($"{nameof(ListenerBackLog)} can't be less than 1", nameof(value));
    }

    private int _listenerBackLog = 511;
}
