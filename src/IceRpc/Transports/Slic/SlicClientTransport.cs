// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Slic.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Slic;

/// <summary>Implements <see cref="IMultiplexedClientTransport" /> using Slic over a duplex client transport.</summary>
public class SlicClientTransport : IMultiplexedClientTransport
{
    /// <inheritdoc/>
    public string DefaultName => _duplexClientTransport.DefaultName;

    /// <inheritdoc/>
    public bool IsSslRequired(string? transportName) => _duplexClientTransport.IsSslRequired(transportName);

    private readonly IDuplexClientTransport _duplexClientTransport;
    private readonly TimeSpan _idleTimeout;
    private readonly int _initialStreamWindowSize;
    private readonly int _maxOutstandingPongs;
    private readonly int _maxStreamFrameSize;
    private readonly int _pauseWriterThreshold;

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="options">The options to configure the Slic transport.</param>
    /// <param name="duplexClientTransport">The duplex client transport.</param>
    public SlicClientTransport(SlicTransportOptions options, IDuplexClientTransport duplexClientTransport)
    {
        _duplexClientTransport = duplexClientTransport;
        _idleTimeout = options.IdleTimeout;
        _initialStreamWindowSize = options.InitialStreamWindowSize;
        _maxOutstandingPongs = options.MaxOutstandingPongs;
        _maxStreamFrameSize = options.MaxStreamFrameSize;
        _pauseWriterThreshold = options.PauseWriterThreshold;
    }

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="duplexClientTransport">The duplex client transport.</param>
    public SlicClientTransport(IDuplexClientTransport duplexClientTransport)
        : this(new SlicTransportOptions(), duplexClientTransport)
    {
    }

    /// <inheritdoc/>
    public IMultiplexedConnection CreateConnection(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions) =>
        new SlicConnection(
            _duplexClientTransport.CreateConnection(
                transportAddress,
                new DuplexConnectionOptions
                {
                    MinSegmentSize = options.MinSegmentSize,
                    Pool = options.Pool
                },
                clientAuthenticationOptions),
            options,
            _idleTimeout,
            _initialStreamWindowSize,
            _maxOutstandingPongs,
            _maxStreamFrameSize,
            _pauseWriterThreshold,
            isServer: false);
}
