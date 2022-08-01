// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;

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

        if (TcpClientTransport.CheckParams(options.Endpoint, out string? endpointTransport))
        {
            if (endpointTransport is null)
            {
                options = options with
                {
                    Endpoint = options.Endpoint with { Params = options.Endpoint.Params.Add("transport", Name) }
                };
            }
            else if (endpointTransport == TransportNames.Ssl &&
                     options.ServerConnectionOptions.ServerAuthenticationOptions is null)
            {
                throw new ArgumentNullException(
                    nameof(options.ServerConnectionOptions.ServerAuthenticationOptions),
                    @$"{nameof(options.ServerConnectionOptions.ServerAuthenticationOptions)
                        } cannot be null with the ssl transport");
            }
        }
        else
        {
            throw new FormatException($"cannot create a TCP listener for endpoint '{options.Endpoint}'");
        }

        return new TcpListener(options, _options);
    }
}
