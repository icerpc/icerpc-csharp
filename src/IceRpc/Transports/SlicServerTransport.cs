// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

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
    public IMultiplexedListener Listen(
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions,
        ILogger logger) =>
        new SlicListener(_duplexServerTransport.Listen(endpoint, authenticationOptions, logger), _slicTransportOptions);
}
