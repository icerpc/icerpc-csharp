// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Slic.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Slic;

/// <summary>Implements <see cref="IMultiplexedServerTransport" /> using Slic over a duplex server transport.</summary>
public class SlicServerTransport : IMultiplexedServerTransport
{
    /// <inheritdoc/>
    public string Name => _duplexServerTransport.Name;

    private readonly IDuplexServerTransport _duplexServerTransport;
    private readonly SlicTransportOptions _slicTransportOptions;

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="options">The options to configure the transport.</param>
    /// <param name="duplexServerTransport">The duplex server transport.</param>
    public SlicServerTransport(SlicTransportOptions options, IDuplexServerTransport duplexServerTransport)
    {
        _slicTransportOptions = options;
        _duplexServerTransport = duplexServerTransport;
    }

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="duplexServerTransport">The duplex server transport.</param>
    public SlicServerTransport(IDuplexServerTransport duplexServerTransport)
        : this(new SlicTransportOptions(), duplexServerTransport)
    {
    }

    /// <inheritdoc/>
    public IListener<IMultiplexedConnection> Listen(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions) =>
        new SlicListener(
            _duplexServerTransport.Listen(
                serverAddress,
                new DuplexConnectionOptions
                {
                    MinSegmentSize = options.MinSegmentSize,
                    Pool = options.Pool
                },
                serverAuthenticationOptions),
            options,
            _slicTransportOptions);
}
