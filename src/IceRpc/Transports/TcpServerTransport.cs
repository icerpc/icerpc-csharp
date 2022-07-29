// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
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
    public IDuplexListener Listen(DuplexListenerOptions options)
    {
        // This is the composition root of the tcp server transport, where we install log decorators when logging
        // is enabled.

        if (options.Endpoint.Params.Count > 0)
        {
            throw new FormatException($"cannot create a TCP listener for endpoint '{options.Endpoint}'");
        }

        if (options.Endpoint.Transport is not string transport)
        {
            options = options with
            {
                Endpoint = options.Endpoint with { Transport = Name }
            };
        }
        else if (transport == TransportNames.Ssl && options.ServerConnectionOptions.ServerAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(options.ServerConnectionOptions.ServerAuthenticationOptions),
                @$"{nameof(options.ServerConnectionOptions.ServerAuthenticationOptions)
                    } cannot be null with the ssl transport");
        }

        return new TcpListener(options, _options);
    }
}
