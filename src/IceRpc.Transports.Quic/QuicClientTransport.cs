// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic.Internal;
using System.Net;
using System.Net.Quic;
using System.Net.Security;

namespace IceRpc.Transports.Quic;

/// <summary>Implements <see cref="IMultiplexedClientTransport"/> using QUIC.</summary>
public class QuicClientTransport : IMultiplexedClientTransport
{
    /// <inheritdoc/>
    public string Name => "quic";

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
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (!QuicConnection.IsSupported)
        {
            throw new NotSupportedException("The Quic client transport is not supported on this platform.");
        }

        if ((serverAddress.Transport is string transport && transport != Name) || !CheckParams(serverAddress))
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains parameters that are not valid for the Quic client transport.",
                nameof(serverAddress));
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }

        clientAuthenticationOptions = clientAuthenticationOptions?.Clone() ?? new();
        clientAuthenticationOptions.TargetHost ??= serverAddress.Host;
        clientAuthenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> // Mandatory with Quic
        {
            new SslApplicationProtocol(serverAddress.Protocol.Name)
        };

        EndPoint endpoint = IPAddress.TryParse(serverAddress.Host, out IPAddress? ipAddress) ?
            new IPEndPoint(ipAddress, serverAddress.Port) :
            new DnsEndPoint(serverAddress.Host, serverAddress.Port);

        var quicClientOptions = new QuicClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            DefaultCloseErrorCode = (int)MultiplexedConnectionCloseError.Aborted,
            DefaultStreamErrorCode = 0,
            IdleTimeout = _quicTransportOptions.IdleTimeout,
#if NET9_0_OR_GREATER
            KeepAliveInterval = _quicTransportOptions.KeepAliveInterval,
#endif
            LocalEndPoint = _quicTransportOptions.LocalNetworkAddress,
            RemoteEndPoint = endpoint,
            MaxInboundBidirectionalStreams = options.MaxBidirectionalStreams,
            MaxInboundUnidirectionalStreams = options.MaxUnidirectionalStreams
        };

        return new QuicMultiplexedClientConnection(options, quicClientOptions);
    }

    private static bool CheckParams(ServerAddress serverAddress) => serverAddress.Params.Count == 0;
}
