// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;

namespace IceRpc.Transports.Quic.Internal;

internal class QuicMultiplexedListener : IListener<IMultiplexedConnection>
{
    public ServerAddress ServerAddress { get; }

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
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        QuicServerTransportOptions quicTransportOptions,
        SslServerAuthenticationOptions authenticationOptions)
    {
        if (!IPAddress.TryParse(serverAddress.Host, out IPAddress? ipAddress))
        {
            throw new ArgumentException(
                $"Listening on the DNS name '{serverAddress.Host}' is not allowed; an IP address is required.",
                nameof(serverAddress));
        }

        _options = options;

        authenticationOptions = authenticationOptions.Clone();
        authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> // Mandatory with Quic
        {
            new SslApplicationProtocol(serverAddress.Protocol.Name)
        };

        _quicServerOptions = new QuicServerConnectionOptions
        {
            DefaultCloseErrorCode = (int)MultiplexedConnectionCloseError.Aborted,
            DefaultStreamErrorCode = 0,
            IdleTimeout = quicTransportOptions.IdleTimeout,
#if NET9_0_OR_GREATER
            KeepAliveInterval = Timeout.InfiniteTimeSpan, // the server doesn't send PING frames
#endif
            ServerAuthenticationOptions = authenticationOptions,
            MaxInboundBidirectionalStreams = options.MaxBidirectionalStreams,
            MaxInboundUnidirectionalStreams = options.MaxUnidirectionalStreams
        };

        try
        {
            // ListenAsync implementation is synchronous so it's safe to get the result synchronously.
            ValueTask<QuicListener> task = QuicListener.ListenAsync(
                new QuicListenerOptions
                {
                    ListenEndPoint = new IPEndPoint(ipAddress, serverAddress.Port),
                    ListenBacklog = quicTransportOptions.ListenBacklog,
                    ApplicationProtocols = authenticationOptions.ApplicationProtocols,
                    ConnectionOptionsCallback = (connection, sslInfo, cancellationToken) => new(_quicServerOptions)
                },
                CancellationToken.None);
            Debug.Assert(task.IsCompleted);
            _listener = task.Result;

            ServerAddress = serverAddress with { Port = (ushort)_listener.LocalEndPoint.Port };
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
