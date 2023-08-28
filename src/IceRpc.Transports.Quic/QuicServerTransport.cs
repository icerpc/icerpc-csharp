// Copyright (c) ZeroC, Inc.

using IceRpc.Transports.Quic.Internal;
using System.Diagnostics;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Net;

namespace IceRpc.Transports.Quic;

/// <summary>Implements <see cref="IMultiplexedServerTransport"/> using QUIC.</summary>
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

#if NET8_0_OR_GREATER
        return new QuicMultiplexedListener(serverAddress, options, _quicOptions, serverAuthenticationOptions);
#else
        return new BugFixQuicMultiplexedListener(
            new QuicMultiplexedListener(serverAddress, options, _quicOptions, serverAuthenticationOptions));
#endif
    }

#if !NET8_0_OR_GREATER
    internal sealed class BugFixQuicMultiplexedListener : IListener<IMultiplexedConnection>
    {
        private readonly IListener<IMultiplexedConnection> _decoratee;

        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        public async Task<(IMultiplexedConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _decoratee.AcceptAsync(cancelationToken);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken != cancellationToken)
            {
                // WORKAROUND QuicListener TLS handshake internal timeout.
                // - https://github.com/dotnet/runtime/issues/78096
                throw new IceRpcException(
                    IceRpcError.IceRpcError,
                    "The QuicListener failed due to TLS handshake internal timeout.");
            }
        }

        public ValueTask DisposeAsync() => _decoratee.DisposeAsync();

        internal BugFixQuicMultiplexedListener(IListener<IMultiplexedConnection> decoratee) => _decoratee = decoratee;
     }
#endif
}
