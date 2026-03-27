// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Tcp.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Tcp;

/// <summary>Implements <see cref="IDuplexServerTransport" /> for the tcp transport.</summary>
public class TcpServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string DefaultName => "tcp";

    /// <inheritdoc/>
    public bool IsSslRequired(string? transportName) => transportName == "ssl";

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
        TransportAddress transportAddress,
        DuplexConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (transportAddress.TransportName is string name && name is not "tcp" and not "ssl")
        {
            throw new NotSupportedException($"The Tcp server transport does not support transport '{name}'.");
        }

        if (transportAddress.TransportName == "ssl" && serverAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(serverAuthenticationOptions),
                "The Tcp server transport requires the Ssl server authentication options to be set for Ssl transport addresses.");
        }

        if (transportAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                "The transport address contains parameters that are not valid for the Tcp server transport.",
                nameof(transportAddress));
        }

        return new TcpListener(transportAddress, options, serverAuthenticationOptions, _options);
    }
}
