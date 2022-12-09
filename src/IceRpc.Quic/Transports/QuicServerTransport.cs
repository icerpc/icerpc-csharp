// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IMultiplexedServerTransport"/> using Quic.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class QuicServerTransport : IMultiplexedServerTransport
{
    /// <inheritdoc/>
    public string Name => "quic";

    private readonly QuicServerTransportOptions _quicOptions;

    /// <summary>Constructs a Quic server transport.</summary>
    /// <param name="options">The options to configure the transport.</param>
    public QuicServerTransport(QuicServerTransportOptions options) => _quicOptions = options;

    /// <summary>Constructs a Quic server transport.</summary>
    public QuicServerTransport()
        : this(new QuicServerTransportOptions())
    {
    }

    /// <inheritdoc/>
    public IListener<IMultiplexedConnection> Listen(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (serverAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                $"The server address contains parameters that are not valid for the Quic server transport: '{serverAddress}'.",
                nameof(serverAddress));
        }

        if (serverAuthenticationOptions is null)
        {
            throw new ArgumentException(
                "The Quic transport requires that the TLS server authentication options are set to a non null value.",
                nameof(serverAuthenticationOptions));
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }

        return new QuicMultiplexedListener(serverAddress, options, _quicOptions, serverAuthenticationOptions);
    }
}
