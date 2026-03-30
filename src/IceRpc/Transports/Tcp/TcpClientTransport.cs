// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Tcp.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Tcp;

/// <summary>Implements <see cref="IDuplexClientTransport" /> for the tcp transport.</summary>
public class TcpClientTransport : IDuplexClientTransport
{
    /// <inheritdoc/>
    public string DefaultName => "tcp";

    /// <inheritdoc/>
    public bool IsSslRequired(string? transportName) => transportName switch
    {
        null or "tcp" => false,
        "ssl" => true,
        _ => throw new NotSupportedException($"The TCP client transport does not support transport '{transportName}'.")
    };

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
        TransportAddress transportAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        // "ssl" is only accepted for the Ice protocol, identified by the ALPN.
        if (transportAddress.TransportName == "ssl")
        {
            if (clientAuthenticationOptions?.ApplicationProtocols is not List<SslApplicationProtocol> alpnProtocols ||
                alpnProtocols.Count != 1 ||
                alpnProtocols[0] != new SslApplicationProtocol("ice"))
            {
                throw new NotSupportedException(
                    "The 'ssl' transport name is only supported with the Ice protocol.");
            }
        }
        else if (transportAddress.TransportName is string name && name != "tcp")
        {
            throw new NotSupportedException($"The TCP client transport does not support transport '{name}'.");
        }

        if (transportAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                "The transport address contains parameters that are not valid for the TCP client transport.",
                nameof(transportAddress));
        }

        SslClientAuthenticationOptions? authenticationOptions = clientAuthenticationOptions?.Clone() ??
            (transportAddress.TransportName == "ssl" ? new SslClientAuthenticationOptions() : null);
        authenticationOptions?.TargetHost ??= transportAddress.Host;

        return new TcpClientConnection(
            transportAddress,
            authenticationOptions,
            options.Pool,
            options.MinSegmentSize,
            _options);
    }
}
