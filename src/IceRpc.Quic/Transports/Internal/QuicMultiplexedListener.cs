// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;

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

    public async Task<(IMultiplexedConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        QuicConnection connection;
        while (true)
        {
            try
            {
                connection = await _listener.AcceptConnectionAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (QuicException exception) when (exception.QuicError == QuicError.OperationAborted)
            {
                // Listener was disposed while accept was in progress.
                throw new TransportException(TransportErrorCode.OperationAborted, exception);
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken != cancellationToken)
            {
                // WORKAROUND QuicListener TLS handshake internal timeout.
                // TODO rework depending on the resolution of:
                // - https://github.com/dotnet/runtime/issues/78096
            }
            catch (QuicException)
            {
                // There was a problem establishing the connection.
                // TODO Log this exception
            }
            catch (AuthenticationException)
            {
                // The connection was rejected due to an authentication exception.
                // TODO Log this exception
            }
        }
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

        authenticationOptions = authenticationOptions.Clone();
        authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol> // Mandatory with Quic
        {
            new SslApplicationProtocol(serverAddress.Protocol.Name)
        };

        _quicServerOptions = new QuicServerConnectionOptions
        {
            DefaultCloseErrorCode = 0,
            DefaultStreamErrorCode = 0,
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
            throw exception.ToTransportException();
        }
    }
}
