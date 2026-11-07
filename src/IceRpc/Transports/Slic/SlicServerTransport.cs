// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Slic.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Slic;

/// <summary>Implements <see cref="IMultiplexedServerTransport" /> using Slic over a duplex server transport.</summary>
public class SlicServerTransport : IMultiplexedServerTransport
{
    /// <inheritdoc/>
    public string DefaultName => _duplexServerTransport.DefaultName;

    private readonly IDuplexServerTransport _duplexServerTransport;
    private readonly TimeSpan _idleTimeout;
    private readonly int _initialStreamWindowSize;
    private readonly int _maxOutstandingPongs;
    private readonly int _maxStreamFrameSize;
    private readonly int _pauseWriterThreshold;

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="options">The options to configure the transport.</param>
    /// <param name="duplexServerTransport">The duplex server transport.</param>
    public SlicServerTransport(SlicTransportOptions options, IDuplexServerTransport duplexServerTransport)
    {
        _duplexServerTransport = duplexServerTransport;
        _idleTimeout = options.IdleTimeout;
        _initialStreamWindowSize = options.InitialStreamWindowSize;
        _maxOutstandingPongs = options.MaxOutstandingPongs;
        _maxStreamFrameSize = options.MaxStreamFrameSize;
        _pauseWriterThreshold = options.PauseWriterThreshold;
    }

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="duplexServerTransport">The duplex server transport.</param>
    public SlicServerTransport(IDuplexServerTransport duplexServerTransport)
        : this(new SlicTransportOptions(), duplexServerTransport)
    {
    }

    /// <inheritdoc/>
    public IListener<IMultiplexedConnection> Listen(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions) =>
        new SlicListener(
            _duplexServerTransport.Listen(
                transportAddress,
                new DuplexConnectionOptions
                {
                    MinSegmentSize = options.MinSegmentSize,
                    Pool = options.Pool
                },
                serverAuthenticationOptions),
            options,
            _idleTimeout,
            _initialStreamWindowSize,
            _maxOutstandingPongs,
            _maxStreamFrameSize,
            _pauseWriterThreshold);
}
