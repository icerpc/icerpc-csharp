// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Tcp.Internal;
using System.Net.Security;

namespace IceRpc.Transports.Tcp;

/// <summary>Implements <see cref="IDuplexServerTransport" /> for the tcp transport.</summary>
public class TcpServerTransport : IDuplexServerTransport
{
    /// <inheritdoc/>
    public string DefaultName => "tcp";

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
        // "ssl" is only accepted for the Ice protocol, identified by the ALPN.
        if (transportAddress.TransportName == "ssl")
        {
            if (serverAuthenticationOptions?.ApplicationProtocols
                is not List<SslApplicationProtocol> alpnProtocols ||
                alpnProtocols.Count != 1 ||
                alpnProtocols[0] != new SslApplicationProtocol("ice"))
            {
                throw new NotSupportedException(
                    "The 'ssl' transport name is only supported with the Ice protocol.");
            }
        }
        else if (transportAddress.TransportName is string name && name != "tcp")
        {
            throw new NotSupportedException($"The TCP server transport does not support transport '{name}'.");
        }

        if (transportAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                "The transport address contains parameters that are not valid for the TCP server transport.",
                nameof(transportAddress));
        }

        return new TcpListener(transportAddress, options, serverAuthenticationOptions, _options);
    }
}
