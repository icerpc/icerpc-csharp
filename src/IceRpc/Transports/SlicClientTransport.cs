// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IClientTransport{IMultiplexedTransportConnection}"/> using Slic over a single stream
/// client transport.</summary>
public class SlicClientTransport : IClientTransport<IMultiplexedTransportConnection>
{
    /// <inheritdoc/>
    public string Name => _singleStreamClientTransport.Name;

    private readonly IClientTransport<ISingleStreamTransportConnection> _singleStreamClientTransport;
    private readonly SlicTransportOptions _slicTransportOptions;

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="options">The options to configure the Slic transport.</param>
    /// <param name="singleStreamClientTransport">The single client transport.</param>
    public SlicClientTransport(
        SlicTransportOptions options,
        IClientTransport<ISingleStreamTransportConnection> singleStreamClientTransport)
    {
        _singleStreamClientTransport = singleStreamClientTransport;
        _slicTransportOptions = options;
    }

    /// <summary>Constructs a Slic client transport.</summary>
    /// <param name="singleStreamClientTransport">The single client transport.</param>
    public SlicClientTransport(IClientTransport<ISingleStreamTransportConnection> singleStreamClientTransport)
        : this(new(), singleStreamClientTransport)
    {
    }

    /// <inheritdoc/>
    public bool CheckParams(Endpoint endpoint) => _singleStreamClientTransport.CheckParams(endpoint);

    /// <inheritdoc/>
    public IMultiplexedTransportConnection CreateConnection(
        Endpoint endpoint,
        SslClientAuthenticationOptions? authenticationOptions,
        ILogger logger)
    {
        IMultiplexedStreamErrorCodeConverter errorCodeConverter =
            endpoint.Protocol.MultiplexedStreamErrorCodeConverter ??
            throw new NotSupportedException(
                $"cannot create Slic client transport connection for protocol {endpoint.Protocol}");

        return new SlicTransportConnection(
            _singleStreamClientTransport.CreateConnection(endpoint, authenticationOptions, logger),
            isServer: false,
            errorCodeConverter,
            _slicTransportOptions);
    }
}
