// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a client connection used to send and receive requests and responses. This client connection is
/// reconnected automatically when its underlying connection is closed by the server or the transport.</summary>
public sealed class ResumableClientConnection : IClientConnection, IAsyncDisposable
{
    /// <inheritdoc/>
    public bool IsInvocable => !IsClosed;

    /// <inheritdoc/>
    public NetworkConnectionInformation? NetworkConnectionInformation => _clientConnection.NetworkConnectionInformation;

    /// <inheritdoc/>
    public Protocol Protocol => _clientConnection.Protocol;

    /// <inheritdoc/>
    public Endpoint RemoteEndpoint => _clientConnection.RemoteEndpoint;

    /// <summary>Gets the state of the connection.</summary>
    public ConnectionState State =>
        _clientConnection.State switch
        {
            ConnectionState.Closed when !IsClosed => ConnectionState.NotConnected,
            ConnectionState state => state
        };

    private bool IsClosed
    {
        get
        {
            lock (_mutex)
            {
                return _isClosed;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
       "Usage",
       "CA2213: Disposable fields should be disposed",
       Justification = "correctly disposed by DisposeAsync, Abort and ShutdownAsync")]
    private ClientConnection _clientConnection;

    private bool _isClosed;

    private readonly ILoggerFactory? _loggerFactory;

    private readonly IClientTransport<IMultiplexedNetworkConnection>? _multiplexedClientTransport;

    private readonly object _mutex = new();

    private Action<IConnection, Exception>? _onClose;

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
        InvokeOnClose(new ConnectionAbortedException());
        _clientConnection.Abort();
    }

    /// <summary>Establishes the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    /// <exception cref="ConnectionClosedException">Thrown if the connection was closed by this client.</exception>
    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        // make a copy of the client connection we're trying with
        ClientConnection clientConnection = _clientConnection;

        try
        {
            await clientConnection.ConnectAsync(cancel).ConfigureAwait(false);
            return;
        }
        catch (ConnectionClosedException) when (!IsClosed)
        {
            RefreshClientConnection(clientConnection);

            // try again with the latest _clientConnection
            await _clientConnection.ConnectAsync(cancel).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() =>
        // Perform a speedy graceful shutdown by canceling invocations and dispatches in progress.
        new(ShutdownAsync("connection disposed", new CancellationToken(canceled: true)));

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        // make a copy of the client connection we're trying with
        ClientConnection clientConnection = _clientConnection;

        try
        {
            return await clientConnection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        catch (ConnectionClosedException) when (!IsClosed)
        {
            RefreshClientConnection(clientConnection);

            // try again with the latest _clientConnection
            return await _clientConnection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        // for retries on other exceptions, the application should use a retry interceptor
    }

    /// <inheritdoc/>
    public void OnClose(Action<IConnection, Exception> callback)
    {
        bool executeCallback = false;

        lock (_mutex)
        {
            if (_isClosed)
            {
                executeCallback = true;
            }
            else
            {
                _onClose += callback;
            }
        }

        if (executeCallback)
        {
            callback(this, new ConnectionClosedException());
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
        InvokeOnClose();
        return _clientConnection.ShutdownAsync(message, cancel);
    }

    private ClientConnection CreateClientConnection()
    {
        var clientConnection = new ClientConnection(
            _options,
            _loggerFactory,
            _multiplexedClientTransport,
            _simpleClientTransport);

        clientConnection.OnClose(OnClose);

        void OnClose(IConnection connection, Exception exception)
        {
            if (!IsClosed)
            {
                RefreshClientConnection((ClientConnection)clientConnection);
            }
        }

        return clientConnection;
    }

    private void InvokeOnClose(Exception? exception = null)
    {
        Action<IConnection, Exception>? onClose = null;

        lock (_mutex)
        {
            if (!_isClosed)
            {
                _isClosed = true;
                onClose = _onClose;
            }
            // else keep onClose null
        }

        onClose?.Invoke(this, exception ?? new ConnectionClosedException());
    }

    private void RefreshClientConnection(ClientConnection clientConnection)
    {
        bool closeOldConnection = false;
        lock (_mutex)
        {
            // We only create a new connection and assign it to _clientConnection if it matches the clientConnection we
            // just tried. If it's another connection, another thread has already called RefreshClientConnection.
            if (clientConnection == _clientConnection)
            {
                _clientConnection = CreateClientConnection();
                closeOldConnection = true;
            }
        }
        if (closeOldConnection)
        {
            // We call ShutdownAsync and not Abort in case we're in the middle of a graceful shutdown initiated by the
            // server.
            _ = clientConnection.ShutdownAsync();
        }
    }
}
