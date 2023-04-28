// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Slic.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Slic;

/// <summary>Implements <see cref="IMultiplexedClientTransport" /> using Slic over a duplex client transport.</summary>
public class SlicClientTransport : IMultiplexedClientTransport
{
    /// <inheritdoc/>
    public string Name => _duplexClientTransport.Name;

    private readonly IDuplexClientTransport _duplexClientTransport;
    private readonly SlicTransportOptions _slicTransportOptions;

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="options">The options to configure the Slic transport.</param>
    /// <param name="duplexClientTransport">The duplex client transport.</param>
    public SlicClientTransport(SlicTransportOptions options, IDuplexClientTransport duplexClientTransport)
    {
        _duplexClientTransport = duplexClientTransport;
        _slicTransportOptions = options;
    }

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="duplexClientTransport">The duplex client transport.</param>
    public SlicClientTransport(IDuplexClientTransport duplexClientTransport)
        : this(new SlicTransportOptions(), duplexClientTransport)
    {
    }

    /// <inheritdoc/>
    public IMultiplexedConnection CreateConnection(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions) =>
        new SlicConnection(
            _duplexClientTransport.CreateConnection(
                serverAddress,
                new DuplexConnectionOptions
                {
                    MinSegmentSize = options.MinSegmentSize,
                    Pool = options.Pool
                },
                clientAuthenticationOptions),
            options,
            _slicTransportOptions,
            isServer: false);
}
