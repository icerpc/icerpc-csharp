// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IServerTransport{IMultiplexedConnection}"/> using Slic over a duplex server
/// transport.</summary>
public class SlicServerTransport : IServerTransport<IMultiplexedConnection>
{
    /// <inheritdoc/>
    public string Name => _duplexServerTransport.Name;

    private readonly IServerTransport<IDuplexConnection> _duplexServerTransport;
    private readonly SlicTransportOptions _slicTransportOptions;

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="options">The options to configure the transport.</param>
    /// <param name="duplexServerTransport">The single server transport.</param>
    public SlicServerTransport(
        SlicTransportOptions options,
        IServerTransport<IDuplexConnection> duplexServerTransport)
    {
        _slicTransportOptions = options;
        _duplexServerTransport = duplexServerTransport;
    }

    /// <summary>Constructs a Slic server transport.</summary>
    /// <param name="duplexServerTransport">The single server transport.</param>
    public SlicServerTransport(IServerTransport<IDuplexConnection> duplexServerTransport)
        : this(new(), duplexServerTransport)
    {
    }

    /// <inheritdoc/>
    public IListener<IMultiplexedConnection> Listen(
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions,
        ILogger logger) =>
        new SlicListener(_duplexServerTransport.Listen(endpoint, authenticationOptions, logger), _slicTransportOptions);
}
