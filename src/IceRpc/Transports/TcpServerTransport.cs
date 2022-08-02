// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IDuplexServerTransport"/> for the tcp and ssl transports.</summary>
public class TcpServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string Name => TransportNames.Tcp;

    private readonly TcpServerTransportOptions _options;

    /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
    public TcpServerTransport()
        : this(new())
    {
    }

    /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
    /// <param name="options">The transport options.</param>
    public TcpServerTransport(TcpServerTransportOptions options) => _options = options;

    /// <inheritdoc/>
    public IDuplexListener Listen(
        Endpoint endpoint,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        // This is the composition root of the tcp server transport, where we install log decorators when logging
        // is enabled.

        if (endpoint.Params.Count > 0)
        {
            throw new FormatException($"cannot create a TCP listener for endpoint '{endpoint}'");
        }

        if (endpoint.Transport is not string transport)
        {
            endpoint = endpoint with { Transport = Name };
        }
        else if (transport == TransportNames.Ssl && serverAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(serverAuthenticationOptions),
                @$"{nameof(serverAuthenticationOptions)} cannot be null with the ssl transport");
        }

        return new TcpListener(endpoint, options, serverAuthenticationOptions, _options);
    }
}
