// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Net;
using System.Net.Quic;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IMultiplexedClientTransport"/> using Quic.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class QuicClientTransport : IMultiplexedClientTransport
{
    /// <inheritdoc/>
    public string Name => "quic";

    private readonly QuicClientTransportOptions _quicTransportOptions;

    /// <summary>Constructs a Quic client transport.</summary>
    /// <param name="options">The options to configure the Quic transport.</param>
    public QuicClientTransport(QuicClientTransportOptions options) => _quicTransportOptions = options;

    /// <summary>Constructs a Quic client transport.</summary>
    public QuicClientTransport()
        : this(new QuicClientTransportOptions())
    {
    }

    /// <inheritdoc/>
    public bool CheckParams(ServerAddress serverAddress) =>
        serverAddress.Protocol == Protocol.IceRpc && serverAddress.Params.Count == 0;

    /// <inheritdoc/>
    public IMultiplexedConnection CreateConnection(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if ((serverAddress.Transport is string transport && transport != Name) ||
            !CheckParams(serverAddress))
        {
            throw new FormatException($"cannot create a Quic connection to server address '{serverAddress}'");
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

        if (options.PayloadExceptionConverter is null)
        {
            throw new ArgumentException("options.PayloadErrorConverter is null", nameof(options));
        }

        // We use the "operation canceled" error code as default error code because that's the error code transmitted
        // when an operation such as stream.ReadAsync or stream.WriteAsync is canceled.
        // See https://github.com/dotnet/runtime/issues/72607.
        var quicClientOptions = new QuicClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            // this works only because the value of PayloadCompleteErrorCode.Canceled is the same as the value of
            // PayloadReadErrorCode.Canceled.
            DefaultStreamErrorCode =
                (long)options.PayloadExceptionConverter.FromInputCompleteException(new OperationCanceledException()),
            DefaultCloseErrorCode = 0,
            IdleTimeout = _quicTransportOptions.IdleTimeout,
            LocalEndPoint = _quicTransportOptions.LocalNetworkAddress,
            RemoteEndPoint = endpoint,
            MaxInboundBidirectionalStreams = options.MaxBidirectionalStreams,
            MaxInboundUnidirectionalStreams = options.MaxUnidirectionalStreams
        };

        return new QuicMultiplexedClientConnection(serverAddress, options, quicClientOptions);
    }
}
