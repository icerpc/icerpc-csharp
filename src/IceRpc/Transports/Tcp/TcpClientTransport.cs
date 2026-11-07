// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using IceRpc.Transports.Tcp.Internal;
using System.Net;
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

    private readonly IPEndPoint? _localNetworkAddress;
    private readonly bool _noDelay;
    private readonly int? _receiveBufferSize;
    private readonly int? _sendBufferSize;

    /// <summary>Constructs a <see cref="TcpClientTransport" />.</summary>
    public TcpClientTransport()
        : this(new TcpClientTransportOptions())
    {
    }

    /// <summary>Constructs a <see cref="TcpClientTransport" />.</summary>
    /// <param name="options">The transport options.</param>
    public TcpClientTransport(TcpClientTransportOptions options)
    {
        _localNetworkAddress = options.LocalNetworkAddress;
        _noDelay = options.NoDelay;
        _receiveBufferSize = options.ReceiveBufferSize;
        _sendBufferSize = options.SendBufferSize;
    }

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(
        TransportAddress transportAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        // "ssl" is only accepted for the ice protocol, identified by the ALPN.
        if (transportAddress.TransportName == "ssl")
        {
            if (clientAuthenticationOptions is null)
            {
                throw new ArgumentNullException(
                    nameof(clientAuthenticationOptions),
                    "The SSL client transport requires the SSL client authentication options to be set.");
            }
            else if (clientAuthenticationOptions.ApplicationProtocols is not List<SslApplicationProtocol> alpnProtocols ||
                alpnProtocols.Count != 1 ||
                alpnProtocols[0] != new SslApplicationProtocol("ice"))
            {
                throw new NotSupportedException("The 'ssl' transport name is only supported with the ice protocol.");
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

        if (clientAuthenticationOptions is not null)
        {
            clientAuthenticationOptions = clientAuthenticationOptions.ShallowClone();
            clientAuthenticationOptions.TargetHost ??= transportAddress.Host;
        }

        return new TcpClientConnection(
            transportAddress,
            clientAuthenticationOptions,
            options.Pool,
            options.MinSegmentSize,
            _localNetworkAddress,
            _noDelay,
            _receiveBufferSize,
            _sendBufferSize);
    }
}
