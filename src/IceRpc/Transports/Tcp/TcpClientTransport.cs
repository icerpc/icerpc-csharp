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

            // Set ApplicationProtocols when the application does not specify any application protocol and the
            // connection options provide one.
            if (authenticationOptions.ApplicationProtocols is null &&
                options.ApplicationProtocol is string applicationProtocol)
            {
                authenticationOptions.ApplicationProtocols = new List<SslApplicationProtocol>
                {
                    new SslApplicationProtocol(applicationProtocol)
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
