// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IServerTransport{IMultiplexedTransportConnection}"/> using Slic over a single stream
/// server transport.</summary>
public class SlicServerTransport : IServerTransport<IMultiplexedTransportConnection>
{
    /// <inheritdoc/>
    public string Name => _singleStreamServerTransport.Name;

    private readonly IServerTransport<ISingleStreamTransportConnection> _singleStreamServerTransport;
    private readonly SlicTransportOptions _slicTransportOptions;

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="options">The options to configure the transport.</param>
    /// <param name="singleStreamServerTransport">The single server transport.</param>
    public SlicServerTransport(
        SlicTransportOptions options,
        IServerTransport<ISingleStreamTransportConnection> singleStreamServerTransport)
    {
        _slicTransportOptions = options;
        _singleStreamServerTransport = singleStreamServerTransport;
    }

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="singleStreamServerTransport">The single server transport.</param>
    public SlicServerTransport(IServerTransport<ISingleStreamTransportConnection> singleStreamServerTransport)
        : this(new(), singleStreamServerTransport)
    {
    }

    /// <inheritdoc/>
    public IListener<IMultiplexedTransportConnection> Listen(
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions,
        ILogger logger) =>
        new SlicListener(
            _singleStreamServerTransport.Listen(endpoint, authenticationOptions, logger),
            _slicTransportOptions);
}
