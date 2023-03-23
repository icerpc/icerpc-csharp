// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Internal;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IMultiplexedServerTransport"/> using Quic.</summary>
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
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                $"The server address '{serverAddress}' contains parameters that are not valid for the Quic transport.");
        }

        if (serverAuthenticationOptions is null)
        {
            throw new IceRpcException(
                IceRpcError.IceRpcError,
                "The Quic transport requires the Ssl server authentication options to be set.");
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }

        return new QuicMultiplexedListener(serverAddress, options, _quicOptions, serverAuthenticationOptions);
    }
}
