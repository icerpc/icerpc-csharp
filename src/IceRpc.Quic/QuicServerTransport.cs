// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IMultiplexedServerTransport"/> using Quic.</summary>
public class QuicServerTransport : IMultiplexedServerTransport
{
    /// <inheritdoc/>
    public string Name => TransportNames.Quic;

    private readonly QuicServerTransportOptions _quicOptions;

    /// <summary>Constructs a Quic server transport.</summary>
    /// <param name="options">The options to configure the transport.</param>
    public QuicServerTransport(QuicServerTransportOptions options) => _quicOptions = options;

    /// <summary>Constructs a Quic server transport.</summary>
    public QuicServerTransport()
        : this(new())
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
            throw new FormatException($"cannot create a TCP listener for server address '{serverAddress}'");
        }

        if (serverAuthenticationOptions is null)
        {
            throw new NotSupportedException("the Quic transport requires TLS server authentication options");
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
        {
            return new QuicMultiplexedListener(serverAddress, options, _quicOptions, serverAuthenticationOptions);
        }
        else
        {
            throw new NotSupportedException("Quic transport is only supported on Linux and Windows");
        }
    }
}
