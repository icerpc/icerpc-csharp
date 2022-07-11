// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IClientTransport{IMultiplexedNetworkConnection}"/> using Slic over a simple
/// client transport.</summary>
public class SlicClientTransport : IClientTransport<IMultiplexedNetworkConnection>
{
    /// <inheritdoc/>
    public string Name => _simpleClientTransport.Name;

    private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;
    private readonly SlicTransportOptions _slicTransportOptions;

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="options">The options to configure the Slic transport.</param>
    /// <param name="simpleClientTransport">The simple client transport.</param>
    public SlicClientTransport(
        SlicTransportOptions options,
        IClientTransport<ISimpleNetworkConnection> simpleClientTransport)
    {
        _simpleClientTransport = simpleClientTransport;
        _slicTransportOptions = options;
    }

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="simpleClientTransport">The simple client transport.</param>
    public SlicClientTransport(IClientTransport<ISimpleNetworkConnection> simpleClientTransport)
        : this(new(), simpleClientTransport)
    {
    }

    /// <inheritdoc/>
    public bool CheckParams(Endpoint endpoint) => _simpleClientTransport.CheckParams(endpoint);

    /// <inheritdoc/>
    public IMultiplexedNetworkConnection CreateConnection(
        Endpoint endpoint,
        SslClientAuthenticationOptions? authenticationOptions,
        ILogger logger)
    {
        IMultiplexedStreamErrorCodeConverter errorCodeConverter =
            endpoint.Protocol.MultiplexedStreamErrorCodeConverter ??
            throw new NotSupportedException(
                $"cannot create Slic client network connection for protocol {endpoint.Protocol}");

        return new SlicNetworkConnection(
            _simpleClientTransport.CreateConnection(endpoint, authenticationOptions, logger),
            isServer: false,
            errorCodeConverter,
            _slicTransportOptions);
    }
}
