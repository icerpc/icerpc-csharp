// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic.Internal;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;

namespace IceRpc.Transports.Quic;

/// <summary>Implements <see cref="IMultiplexedClientTransport"/> using QUIC.</summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public class QuicClientTransport : IMultiplexedClientTransport
{
    /// <inheritdoc/>
    public string DefaultName => "quic";

    /// <inheritdoc/>
    public bool IsSslRequired(string? transportName) => true;

    private readonly QuicClientTransportOptions _quicTransportOptions;

    /// <summary>Constructs a QUIC client transport.</summary>
    /// <param name="options">The options to configure the QUIC client transport.</param>
    public QuicClientTransport(QuicClientTransportOptions options) => _quicTransportOptions = options;

    /// <summary>Constructs a QUIC client transport.</summary>
    public QuicClientTransport()
        : this(new QuicClientTransportOptions())
    {
    }

    /// <inheritdoc/>
    public IMultiplexedConnection CreateConnection(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (!QuicConnection.IsSupported)
        {
            throw new NotSupportedException(
                "The QUIC client transport is not available on this system. Please review the Platform Dependencies for QUIC in the .NET documentation.");
        }

        if (transportAddress.TransportName is string name && name != DefaultName)
        {
            throw new NotSupportedException($"The QUIC client transport does not support transport '{name}'.");
        }

        if (transportAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                "The transport address contains parameters that are not valid for the QUIC client transport.",
                nameof(transportAddress));
        }

        if (clientAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(clientAuthenticationOptions),
                "The QUIC client transport requires the SSL client authentication options to be set.");
        }

        if (clientAuthenticationOptions.ApplicationProtocols is null or { Count: 0 })
        {
            throw new ArgumentException(
                "The QUIC client transport requires ApplicationProtocols to be set on the SSL client authentication options.",
                nameof(clientAuthenticationOptions));
        }

        if (clientAuthenticationOptions.TargetHost is null)
        {
            clientAuthenticationOptions = clientAuthenticationOptions.Clone();
            clientAuthenticationOptions.TargetHost = transportAddress.Host;
        }

        EndPoint endPoint = IPAddress.TryParse(transportAddress.Host, out IPAddress? ipAddress) ?
            new IPEndPoint(ipAddress, transportAddress.Port) :
            new DnsEndPoint(transportAddress.Host, transportAddress.Port);

        var quicClientOptions = new QuicClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            DefaultCloseErrorCode = (int)MultiplexedConnectionCloseError.Aborted,
            DefaultStreamErrorCode = 0,
            HandshakeTimeout = options.HandshakeTimeout,
            IdleTimeout = _quicTransportOptions.IdleTimeout,
            InitialReceiveWindowSizes = _quicTransportOptions.InitialReceiveWindowSizes,
            KeepAliveInterval = _quicTransportOptions.KeepAliveInterval,
            LocalEndPoint = _quicTransportOptions.LocalNetworkAddress,
            RemoteEndPoint = endPoint,
            MaxInboundBidirectionalStreams = options.MaxBidirectionalStreams,
            MaxInboundUnidirectionalStreams = options.MaxUnidirectionalStreams
        };

        return new QuicMultiplexedClientConnection(options, quicClientOptions);
    }
}
