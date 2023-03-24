// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IDuplexServerTransport" /> for the tcp and ssl transports.</summary>
public class TcpServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string Name => TransportNames.Tcp;

    private readonly TcpServerTransportOptions _options;

    /// <summary>Constructs a <see cref="TcpServerTransport" />.</summary>
    public TcpServerTransport()
        : this(new TcpServerTransportOptions())
    {
    }

    /// <summary>Constructs a <see cref="TcpServerTransport" />.</summary>
    /// <param name="options">The transport options.</param>
    public TcpServerTransport(TcpServerTransportOptions options) => _options = options;

    /// <inheritdoc/>
    public IListener<IDuplexConnection> Listen(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if ((serverAddress.Transport is string transport &&
            transport != TransportNames.Tcp &&
            transport != TransportNames.Ssl) ||
            serverAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains parameters that are not valid for the Tcp server transport.",
                nameof(serverAddress));
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }
        else if (serverAddress.Transport == TransportNames.Ssl && serverAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(serverAuthenticationOptions),
                "The Ssl server transport requires the Ssl server authentication options to be set.");
        }

        return new TcpListener(serverAddress, options, serverAuthenticationOptions, _options);
    }
}
