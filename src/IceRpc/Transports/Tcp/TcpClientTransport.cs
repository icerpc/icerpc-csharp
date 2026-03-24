// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Tcp.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Tcp;

/// <summary>Implements <see cref="IDuplexClientTransport" /> for the tcp transport.</summary>
public class TcpClientTransport : IDuplexClientTransport
{
    /// <inheritdoc/>
    public bool IsSslRequired(string? transportName) => transportName == "ssl";

    /// <inheritdoc/>
    public string Name => "tcp";

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
        if (transportAddress.Name is string name && name is not "tcp" and not "ssl")
        {
            throw new NotSupportedException(
                $"The TCP client transport does not support transport '{name}'.");
        }

        if (transportAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                "The transport address contains parameters that are not valid for the TCP client transport.",
                nameof(transportAddress));
        }

        SslClientAuthenticationOptions? authenticationOptions = clientAuthenticationOptions?.Clone();

        authenticationOptions?.TargetHost ??= transportAddress.Host;

        return new TcpClientConnection(
            transportAddress,
            authenticationOptions,
            options.Pool,
            options.MinSegmentSize,
            _options);
    }
}
