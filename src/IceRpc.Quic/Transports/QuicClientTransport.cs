// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Net;
using System.Net.Quic;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IMultiplexedClientTransport"/> using Quic.</summary>
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
        SslClientAuthenticationOptions? authenticationOptions)
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

        authenticationOptions = authenticationOptions?.Clone() ?? new();
        authenticationOptions.TargetHost ??= serverAddress.Host;
        authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> // Mandatory with Quic
            {
                new SslApplicationProtocol(serverAddress.Protocol.Name)
            };

        EndPoint endpoint = IPAddress.TryParse(serverAddress.Host, out IPAddress? ipAddress) ?
            new IPEndPoint(ipAddress, serverAddress.Port) :
            new DnsEndPoint(serverAddress.Host, serverAddress.Port);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
        {
            // We use the maximum value for DefaultStreamErrorCode to ensure that the abort on the peer will show this
            // value if the stream is aborted when we do not expect it (https://github.com/dotnet/runtime/issues/72607)
            var quicClientOptions = new QuicClientConnectionOptions
                {
                    ClientAuthenticationOptions = authenticationOptions,
                    DefaultStreamErrorCode = (1L << 62) - 1,
                    DefaultCloseErrorCode = 0,
                    IdleTimeout = _quicTransportOptions.IdleTimeout,
                    LocalEndPoint = _quicTransportOptions.LocalNetworkAddress,
                    RemoteEndPoint = endpoint,
                    MaxInboundBidirectionalStreams = options.MaxBidirectionalStreams,
                    MaxInboundUnidirectionalStreams = options.MaxUnidirectionalStreams
                };

            return new QuicMultiplexedClientConnection(serverAddress, options, _quicTransportOptions, quicClientOptions);
        }
        else
        {
            throw new NotSupportedException("the Quic transport is only supported on Linux and Windows");
        }
    }
}
