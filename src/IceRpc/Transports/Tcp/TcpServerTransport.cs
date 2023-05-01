// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Tcp.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Tcp;

/// <summary>Implements <see cref="IDuplexServerTransport" /> for the tcp transport.</summary>
public class TcpServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string Name => TcpName;

    private const string SslName = "ssl";
    private const string TcpName = "tcp";

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
        if (serverAddress.Transport is string transport && !IsValidTransportName(transport, serverAddress.Protocol))
        {
            throw new NotSupportedException(
                $"The Tcp server transport does not support server addresses with transport '{transport}'.");
        }

        if (serverAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains parameters that are not valid for the Tcp server transport.",
                nameof(serverAddress));
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }
        else if (serverAddress.Transport == SslName && serverAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(serverAuthenticationOptions),
                "The Ssl server transport requires the Ssl server authentication options to be set.");
        }

        return new TcpListener(serverAddress, options, serverAuthenticationOptions, _options);

        static bool IsValidTransportName(string transportName, Protocol protocol) =>
            protocol == Protocol.Ice ? transportName is TcpName or SslName : transportName is TcpName;
    }
}
