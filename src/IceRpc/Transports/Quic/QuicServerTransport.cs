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
    public bool IsSslRequired(string? transportName) => true;

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
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (!QuicListener.IsSupported)
        {
            throw new NotSupportedException(
                "The QUIC server transport is not available on this system. Please review the Platform Dependencies for QUIC in the .NET documentation.");
        }

        if (transportAddress.TransportName is string name && name != Name)
        {
            throw new NotSupportedException(
                $"The QUIC server transport does not support transport '{name}'.");
        }

        if (transportAddress.Params.Count > 0)
        {
            throw new ArgumentException(
                "The transport address contains parameters that are not valid for the QUIC server transport.",
                nameof(transportAddress));
        }

        if (serverAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(serverAuthenticationOptions),
                "The QUIC server transport requires the SSL server authentication options to be set.");
        }

        if (serverAuthenticationOptions.ApplicationProtocols is null or { Count: 0 })
        {
            throw new ArgumentException(
                "The QUIC server transport requires ApplicationProtocols to be set on the SSL server authentication options.",
                nameof(serverAuthenticationOptions));
        }

        return new QuicMultiplexedListener(transportAddress, options, _quicOptions, serverAuthenticationOptions);
    }
}
