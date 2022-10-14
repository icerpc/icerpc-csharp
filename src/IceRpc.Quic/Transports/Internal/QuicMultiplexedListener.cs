// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Security;

namespace IceRpc.Transports.Internal;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal class QuicMultiplexedListener : IListener<IMultiplexedConnection>
{
    public ServerAddress ServerAddress { get; }

    private readonly QuicListener _listener;
    private readonly MultiplexedConnectionOptions _options;
    private readonly QuicServerConnectionOptions _quicServerOptions;
    private readonly QuicTransportOptions _quicTransportOptions;

    public async Task<(IMultiplexedConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        QuicConnection connection = await _listener.AcceptConnectionAsync(cancellationToken).ConfigureAwait(false);
        return (new QuicMultiplexedServerConnection(ServerAddress, connection, _options), connection.RemoteEndPoint);
    }

    public ValueTask DisposeAsync() => _listener.DisposeAsync();

    internal QuicMultiplexedListener(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        QuicServerTransportOptions quicTransportOptions,
        SslServerAuthenticationOptions authenticationOptions)
    {
        _options = options;
        _quicTransportOptions = quicTransportOptions;

        authenticationOptions = authenticationOptions.Clone();
        authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> // Mandatory with Quic
            {
                new SslApplicationProtocol(serverAddress.Protocol.Name)
            };

        // We use the maximum value for DefaultStreamErrorCode to ensure that the abort on the peer will show this
        // value if the stream is aborted when we do not expect it (https://github.com/dotnet/runtime/issues/72607)
        _quicServerOptions = new QuicServerConnectionOptions
            {
                DefaultStreamErrorCode = (1L << 62) - 1,
                DefaultCloseErrorCode = 0,
                IdleTimeout = quicTransportOptions.IdleTimeout,
                ServerAuthenticationOptions = authenticationOptions,
                MaxInboundBidirectionalStreams = options.MaxBidirectionalStreams,
                MaxInboundUnidirectionalStreams = options.MaxUnidirectionalStreams
            };

        if (!IPAddress.TryParse(serverAddress.Host, out IPAddress? ipAddress))
        {
            throw new NotSupportedException(
                $"serverAddress '{serverAddress}' cannot accept connections because it has a DNS name");
        }

        // ListenAsync implementation is synchronous so it's safe to get the result synchronously.
        ValueTask<QuicListener> task = QuicListener.ListenAsync(
            new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(ipAddress, serverAddress.Port),
                ListenBacklog = quicTransportOptions.ListenerBackLog,
                ApplicationProtocols = authenticationOptions.ApplicationProtocols,
                ConnectionOptionsCallback = GetConnectionOptionsAsync
            },
            CancellationToken.None);

        Debug.Assert(task.IsCompleted);
        _listener = task.Result;

        ServerAddress = serverAddress with { Port = (ushort)_listener.LocalEndPoint.Port };
    }

    private ValueTask<QuicServerConnectionOptions> GetConnectionOptionsAsync(
        QuicConnection connection,
        SslClientHelloInfo sslInfo,
        CancellationToken cancellationToken) =>
        // TODO: Provide functionality to validate the SSL client hello message here? Does Quic run the
        // SslServerAuthenticationOptions.RemoteCertificateValidationCallback?
        new(_quicServerOptions);
}
