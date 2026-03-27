// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace IceRpc.Transports.Quic.Internal;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
internal class QuicMultiplexedListener : IListener<IMultiplexedConnection>
{
    public TransportAddress TransportAddress { get; }

    private readonly QuicListener _listener;
    private readonly MultiplexedConnectionOptions _options;
    private readonly QuicServerConnectionOptions _quicServerOptions;

    public async Task<(IMultiplexedConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        try
        {
            QuicConnection connection = await _listener.AcceptConnectionAsync(cancellationToken).ConfigureAwait(false);
            return (new QuicMultiplexedServerConnection(connection, _options), connection.RemoteEndPoint);
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
    }

    public ValueTask DisposeAsync() => _listener.DisposeAsync();

    internal QuicMultiplexedListener(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        QuicServerTransportOptions quicTransportOptions,
        SslServerAuthenticationOptions serverAuthenticationOptions)
    {
        if (!IPAddress.TryParse(transportAddress.Host, out IPAddress? ipAddress))
        {
            throw new ArgumentException(
                $"Listening on the DNS name '{transportAddress.Host}' is not allowed; an IP address is required.",
                nameof(transportAddress));
        }

        if (serverAuthenticationOptions is null)
        {
            throw new ArgumentNullException(
                nameof(serverAuthenticationOptions),
                "The QUIC server transport requires the SSL server authentication options to be set.");
        }
        serverAuthenticationOptions = serverAuthenticationOptions.Clone();

        if (serverAuthenticationOptions.ApplicationProtocols
            is not List<SslApplicationProtocol> applicationProtocols || applicationProtocols.Count == 0)
        {
            throw new ArgumentException(
                "The QUIC server transport requires ApplicationProtocols to be set in the SSL server authentication options.",
                nameof(serverAuthenticationOptions));
        }

        _options = options;

        _quicServerOptions = new QuicServerConnectionOptions
        {
            DefaultCloseErrorCode = (int)MultiplexedConnectionCloseError.Aborted,
            DefaultStreamErrorCode = 0,
            HandshakeTimeout = options.HandshakeTimeout,
            IdleTimeout = quicTransportOptions.IdleTimeout,
            KeepAliveInterval = Timeout.InfiniteTimeSpan, // the server doesn't send PING frames
            InitialReceiveWindowSizes = quicTransportOptions.InitialReceiveWindowSizes,
            ServerAuthenticationOptions = serverAuthenticationOptions,
            MaxInboundBidirectionalStreams = options.MaxBidirectionalStreams,
            MaxInboundUnidirectionalStreams = options.MaxUnidirectionalStreams
        };

        try
        {
            // ListenAsync implementation is synchronous so it's safe to get the result synchronously.
            ValueTask<QuicListener> task = QuicListener.ListenAsync(
                new QuicListenerOptions
                {
                    ListenEndPoint = new IPEndPoint(ipAddress, transportAddress.Port),
                    ListenBacklog = quicTransportOptions.ListenBacklog,
                    ApplicationProtocols = serverAuthenticationOptions.ApplicationProtocols,
                    ConnectionOptionsCallback = (connection, sslInfo, cancellationToken) => new(_quicServerOptions)
                },
                CancellationToken.None);
            Debug.Assert(task.IsCompleted);
            _listener = task.Result;

            TransportAddress = transportAddress with { Port = (ushort)_listener.LocalEndPoint.Port };
        }
        catch (QuicException exception)
        {
            throw exception.ToIceRpcException();
        }
        catch (SocketException exception)
        {
            throw exception.ToIceRpcException();
        }
    }
}
