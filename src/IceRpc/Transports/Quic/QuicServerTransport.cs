// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic.Internal;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;

namespace IceRpc.Transports.Quic;

/// <summary>Implements <see cref="IMultiplexedServerTransport"/> using QUIC.</summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public class QuicServerTransport : IMultiplexedServerTransport
{
    /// <inheritdoc/>
    public string Name => "quic";

    private readonly QuicServerTransportOptions _quicOptions;

    /// <summary>Constructs a QUIC server transport.</summary>
    /// <param name="options">The options to configure the transport.</param>
    public QuicServerTransport(QuicServerTransportOptions options) => _quicOptions = options;

    /// <summary>Constructs a QUIC server transport.</summary>
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
        if (!QuicConnection.IsSupported)
        {
            throw new NotSupportedException("The Quic server transport is not supported on this platform.");
        }

        if ((serverAddress.Transport is string transport && transport != Name) || serverAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains parameters that are not valid for the Quic server transport.",
                nameof(serverAddress));
        }

        if (serverAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(serverAuthenticationOptions),
                "The Quic server transport requires the Ssl server authentication options to be set.");
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }

        return new QuicMultiplexedListener(serverAddress, options, _quicOptions, serverAuthenticationOptions);
    }
}
