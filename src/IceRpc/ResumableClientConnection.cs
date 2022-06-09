// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a client connection used to send and receive requests and responses. This client connection
/// can be reconnected when the connection is closed by the server or the underlying transport.</summary>
public sealed class ResumableClientConnection : IClientConnection, IAsyncDisposable
{
    /// <inheritdoc/>
    public bool IsInvocable => !_isClosed;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation => _clientConnection.NetworkConnectionInformation;

    /// <inheritdoc/>
    public Protocol Protocol => _clientConnection.Protocol;

    /// <inheritdoc/>
    public Endpoint RemoteEndpoint => _clientConnection.RemoteEndpoint;

    /// <summary>Gets the state of the connection.</summary>
    public ConnectionState State =>
        _clientConnection.State is ConnectionState state &&
        state == ConnectionState.Closed &&
        !_isClosed ? ConnectionState.NotConnected : state;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
       "Usage",
       "CA2213: Disposable fields should be disposed",
       Justification = "correctly disposed by DisposeAsync, Abort and ShutdownAsync")]
    private ClientConnection _clientConnection;

    private volatile bool _isClosed;

    private readonly ILoggerFactory? _loggerFactory;

    private readonly IClientTransport<IMultiplexedNetworkConnection>? _multiplexedClientTransport;

    private readonly object _mutex = new();

    private readonly ClientConnectionOptions _options;

    private readonly IClientTransport<ISimpleNetworkConnection>? _simpleClientTransport;

    /// <summary>Constructs a resumable client connection.</summary>
    /// <param name="options">The client connection options.</param>
    /// <param name="loggerFactory">The logger factory used to create loggers to log connection-related activities.
    /// </param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol connections.
    /// </param>
    /// <param name="simpleClientTransport">The simple transport used to create ice protocol connections.</param>
    public ResumableClientConnection(
        ClientConnectionOptions options,
        ILoggerFactory? loggerFactory = null,
        IClientTransport<IMultiplexedNetworkConnection>? multiplexedClientTransport = null,
        IClientTransport<ISimpleNetworkConnection>? simpleClientTransport = null)
    {
        _options = options;
        _multiplexedClientTransport = multiplexedClientTransport;
        _simpleClientTransport = simpleClientTransport;
        _loggerFactory = loggerFactory;

        _clientConnection = CreateClientConnection();
    }

    /// <summary>Constructs a resumable client connection with the specified remote endpoint and client authentication
    /// options. All other properties have their default values.</summary>
    /// <param name="endpoint">The connection remote endpoint.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ResumableClientConnection(
        Endpoint endpoint,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new ClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            RemoteEndpoint = endpoint
        })
    {
    }

    /// <summary>Aborts the connection. This methods switches the connection state to <see
    /// cref="ConnectionState.Closed"/>.</summary>
    public void Abort()
    {
        _isClosed = true;
        _clientConnection.Abort();
    }

    /// <summary>Establishes the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    /// <exception cref="ConnectionClosedException">Thrown if the connection was closed by this client.</exception>
    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        while (true)
        {
            ClientConnection clientConnection = _clientConnection;

            try
            {
                await clientConnection.ConnectAsync(cancel).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is ConnectionLostException or ConnectionClosedException)
            {
                if (_isClosed)
                {
                    if (exception is ConnectionClosedException)
                    {
                        throw;
                    }
                    else
                    {
                        throw new ConnectionClosedException();
                    }
                }
                else
                {
                    bool disposeOldConnection = false;
                    lock (_mutex)
                    {
                        // We only create a new connection and assign it to _clientConnection if we tried with
                        // _clientConnection. If false, another thread could has done the same and we want to retry
                        // with the new _clientConnection.
                        if (clientConnection == _clientConnection)
                        {
                            _clientConnection = CreateClientConnection();
                            disposeOldConnection = true;
                        }
                    }
                    if (disposeOldConnection)
                    {
                        await clientConnection.DisposeAsync().ConfigureAwait(false);
                    }

                    // and try again
                }
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() =>
        // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
        new(ShutdownAsync("connection disposed", new CancellationToken(canceled: true)));

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        // TODO: refactor to avoid duplication with ConnectAsync code
        while (true)
        {
            ClientConnection clientConnection = _clientConnection;

            try
            {
                return await clientConnection.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
            catch (ConnectionClosedException)
            {
                if (_isClosed)
                {
                    throw;
                }
                else
                {
                    bool disposeOldConnection = false;
                    lock (_mutex)
                    {
                        // We only create a new connection and assign it to _clientConnection if we tried with
                        // _clientConnection. If false, another thread could has done the same and we want to retry
                        // with the new _clientConnection.
                        if (clientConnection == _clientConnection)
                        {
                            _clientConnection = CreateClientConnection();
                            disposeOldConnection = true;
                        }
                    }
                    if (disposeOldConnection)
                    {
                        await clientConnection.DisposeAsync().ConfigureAwait(false);
                    }

                    // and try again
                }
            }
        }
    }

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    public Task ShutdownAsync(CancellationToken cancel = default) => ShutdownAsync("connection shutdown", cancel);

    /// <summary>Gracefully shuts down of the connection. If ShutdownAsync is canceled, dispatch and invocations are
    /// canceled. Shutdown cancellation can lead to a speedier shutdown if dispatch are cancelable.</summary>
    /// <param name="message">The message transmitted to the peer (when using the IceRPC protocol).</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    public Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        _isClosed = true;
        return _clientConnection.ShutdownAsync(message, cancel);
    }

    // TODO: we need to register a callback with the client connection to be notified when it dies so that we can
    // immediately replace it with a fresh connection.
    private ClientConnection CreateClientConnection() =>
        new(_options, _loggerFactory, _multiplexedClientTransport, _simpleClientTransport);
}
