// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IServerTransport{IMultiplexedNetworkConnection}"/> using Slic over a simple
/// server transport.</summary>
public class SlicServerTransport : IServerTransport<IMultiplexedNetworkConnection>
{
    /// <inheritdoc/>
    public string Name => _simpleServerTransport.Name;

    private readonly IServerTransport<ISimpleNetworkConnection> _simpleServerTransport;
    private readonly SlicTransportOptions _slicTransportOptions;

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="options">The options to configure the transport.</param>
    /// <param name="simpleServerTransport">The simple server transport.</param>
    public SlicServerTransport(
        SlicTransportOptions options,
        IServerTransport<ISimpleNetworkConnection> simpleServerTransport)
    {
        _slicTransportOptions = options;
        _simpleServerTransport = simpleServerTransport;
    }

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="simpleServerTransport">The simple server transport.</param>
    public SlicServerTransport(IServerTransport<ISimpleNetworkConnection> simpleServerTransport)
        : this(new(), simpleServerTransport)
    {
    }

    /// <inheritdoc/>
    public IListener<IMultiplexedNetworkConnection> Listen(
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions,
        ILogger logger) =>
        new SlicListener(_simpleServerTransport.Listen(endpoint, authenticationOptions, logger), _slicTransportOptions);
}
