// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Tcp.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Tcp;

/// <summary>Implements <see cref="IDuplexClientTransport" /> for the tcp transport.</summary>
public class TcpClientTransport : IDuplexClientTransport
{
    /// <inheritdoc/>
    public string Name => TcpName;

    private const string SslName = "ssl";
    private const string TcpName = "tcp";

    private readonly TcpClientTransportOptions _options;

    /// <summary>Constructs a <see cref="TcpClientTransport" />.</summary>
    public TcpClientTransport()
        : this(new TcpClientTransportOptions())
    {
    }

    /// <summary>Constructs a <see cref="TcpClientTransport" />.</summary>
    /// <param name="options">The transport options.</param>
    public TcpClientTransport(TcpClientTransportOptions options) => _options = options;

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (serverAddress.Transport is string transport && !IsValidTransportName(transport, serverAddress.Protocol))
        {
            throw new NotSupportedException(
                $"The Tcp client transport does not support server addresses with transport '{transport}'.");
        }

        if (!CheckParams(serverAddress))
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains parameters that are not valid for the Tcp client transport.",
                nameof(serverAddress));
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }

        SslClientAuthenticationOptions? authenticationOptions = clientAuthenticationOptions?.Clone() ??
            (serverAddress.Transport == SslName ? new SslClientAuthenticationOptions() : null);

        if (authenticationOptions is not null)
        {
            // We are establishing a secure TLS connection. It can rely on system certificates.

            authenticationOptions.TargetHost ??= serverAddress.Host;

            // Set ApplicationProtocols to "ice" or "icerpc" in the common situation where the application does not
            // specify any application protocol. This way, a proxy server listening on a port shared by multiple
            // application protocols can use this ALPN protocol ID to forward all ice/icerpc traffic to an ice/icerpc
            // back-end server.
            // We do this only when the port is not the default port for ice or icerpc; when we use the IANA-registered
            // default port, the server can and should use this port number to identify the application protocol when no
            // ALPN protocol ID is provided.
            if (authenticationOptions.ApplicationProtocols is null &&
                serverAddress.Port != serverAddress.Protocol.DefaultPort)
            {
                authenticationOptions.ApplicationProtocols = new List<SslApplicationProtocol>
                {
                    new SslApplicationProtocol(serverAddress.Protocol.Name)
                };
            }
        }

        return new TcpClientConnection(
            serverAddress,
            authenticationOptions,
            options.Pool,
            options.MinSegmentSize,
            _options);

        static bool CheckParams(ServerAddress serverAddress)
        {
            if (serverAddress.Protocol == Protocol.Ice)
            {
                foreach (string name in serverAddress.Params.Keys)
                {
                    switch (name)
                    {
                        case "t":
                        case "z":
                            // we don't check the value since we ignore it
                            break;

                        default:
                            return false;
                    }
                }
                return true;
            }
            else
            {
                return serverAddress.Params.Count == 0;
            }
        }

        static bool IsValidTransportName(string transportName, Protocol protocol) =>
            protocol == Protocol.Ice ? transportName is TcpName or SslName : transportName is TcpName;
    }
}
