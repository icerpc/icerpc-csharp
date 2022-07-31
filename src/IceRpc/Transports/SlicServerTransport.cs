// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IMultiplexedServerTransport"/> using Slic over a duplex server
/// transport.</summary>
public class SlicServerTransport : IMultiplexedServerTransport
{
    /// <inheritdoc/>
    public string Name => _duplexServerTransport.Name;

    private readonly IDuplexServerTransport _duplexServerTransport;
    private readonly SlicTransportOptions _slicTransportOptions;

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="options">The options to configure the transport.</param>
    /// <param name="duplexServerTransport">The single server transport.</param>
    public SlicServerTransport(
        SlicTransportOptions options,
        IDuplexServerTransport duplexServerTransport)
    {
        _slicTransportOptions = options;
        _duplexServerTransport = duplexServerTransport;
    }

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="duplexServerTransport">The single server transport.</param>
    public SlicServerTransport(IDuplexServerTransport duplexServerTransport)
        : this(new(), duplexServerTransport)
    {
    }

    /// <inheritdoc/>
    public IMultiplexedListener Listen(MultiplexedListenerOptions options)
    {
        // TODO: temporary until #1536 is fixed
        IDuplexServerTransport duplexServerTransport = _duplexServerTransport;

        return new SlicListener(
            duplexServerTransport.Listen(
                new DuplexListenerOptions
                {
                    ServerConnectionOptions = new()
                    {
                        MinSegmentSize = options.ServerConnectionOptions.MinSegmentSize,
                        Pool = options.ServerConnectionOptions.Pool,
                        ServerAuthenticationOptions = options.ServerConnectionOptions.ServerAuthenticationOptions
                    },
                    Endpoint = options.Endpoint,
                    Logger = options.Logger
                }),
            options,
            _slicTransportOptions);
    }
}
