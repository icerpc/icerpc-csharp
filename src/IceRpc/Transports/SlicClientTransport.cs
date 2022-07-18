// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IClientTransport{IMultiplexedConnection}"/> using Slic over a duplex client
/// transport.</summary>
public class SlicClientTransport : IMultiplexedClientTransport
{
    /// <inheritdoc/>
    public string Name => _duplexClientTransport.Name;

    private readonly IDuplexClientTransport _duplexClientTransport;
    private readonly SlicTransportOptions _slicTransportOptions;

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="options">The options to configure the Slic transport.</param>
    /// <param name="duplexClientTransport">The single client transport.</param>
    public SlicClientTransport(
        SlicTransportOptions options,
        IDuplexClientTransport duplexClientTransport)
    {
        _duplexClientTransport = duplexClientTransport;
        _slicTransportOptions = options;
    }

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="duplexClientTransport">The single client transport.</param>
    public SlicClientTransport(IDuplexClientTransport duplexClientTransport)
        : this(new(), duplexClientTransport)
    {
    }

    /// <inheritdoc/>
    public bool CheckParams(Endpoint endpoint) => _duplexClientTransport.CheckParams(endpoint);

    /// <inheritdoc/>
    public IMultiplexedConnection CreateConnection(
        Endpoint endpoint,
        SslClientAuthenticationOptions? authenticationOptions,
        ILogger logger)
    {
        IMultiplexedStreamErrorCodeConverter errorCodeConverter =
            endpoint.Protocol.MultiplexedStreamErrorCodeConverter ??
            throw new NotSupportedException(
                $"cannot create Slic client transport connection for protocol {endpoint.Protocol}");

        return new SlicMultiplexedConnection(
            _duplexClientTransport.CreateConnection(endpoint, authenticationOptions, logger),
            isServer: false,
            errorCodeConverter,
            _slicTransportOptions);
    }
}
