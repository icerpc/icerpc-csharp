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
    public IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        // This is the composition root of the tcp server transport, where we install log decorators when logging
        // is enabled.

        if (serverAddress.Params.Count > 0)
        {
            throw new FormatException($"cannot create a TCP listener for server address '{serverAddress}'");
        }

        if (serverAddress.Transport is not string transport)
        {
            serverAddress = serverAddress with { Transport = Name };
        }
        else if (transport == TransportNames.Ssl && serverAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(serverAuthenticationOptions),
                $"{nameof(serverAuthenticationOptions)} cannot be null with the ssl transport");
        }

        return new TcpListener(serverAddress, options, serverAuthenticationOptions, _options);
    }
}
