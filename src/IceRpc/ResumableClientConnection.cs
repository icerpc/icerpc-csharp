// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a client connection used to send and receive requests and responses. This client connection is
/// reconnected automatically when its underlying connection is closed by the server or the transport.</summary>
public sealed class ResumableClientConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the endpoint of this connection. This endpoint includes a transport parameter even when
    /// <see cref="ClientConnectionOptions.Endpoint"/> does not.</summary>
    public Endpoint Endpoint => _clientConnection.Endpoint;

    /// <summary>Gets the protocol of this connection.</summary>
    public Protocol Protocol => _clientConnection.Protocol;

    private bool IsResumable
    {
        get
        {
            lock (_mutex)
            {
                return _isResumable;
            }
        }
    }

    private ClientConnection _clientConnection;

    private bool _isResumable = true;

    private readonly ILoggerFactory? _loggerFactory;

    private readonly IMultiplexedClientTransport? _multiplexedClientTransport;

    private readonly object _mutex = new();

    private Action<Exception>? _onAbort;

    private Action<string>? _onShutdown;

    private readonly ClientConnectionOptions _options;

    private readonly IDuplexClientTransport? _duplexClientTransport;

    /// <summary>Constructs a resumable client connection.</summary>
    /// <param name="options">The client connection options.</param>
    /// <param name="loggerFactory">The logger factory used to create loggers to log connection-related activities.
    /// </param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol connections.
    /// </param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice protocol connections.</param>
    public ResumableClientConnection(
        ClientConnectionOptions options,
        ILoggerFactory? loggerFactory = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        IDuplexClientTransport? duplexClientTransport = null)
    {
        _options = options;
        _multiplexedClientTransport = multiplexedClientTransport;
        _duplexClientTransport = duplexClientTransport;
        _loggerFactory = loggerFactory;

        _clientConnection = CreateClientConnection();
    }

    /// <summary>Constructs a resumable client connection with the specified endpoint and client authentication options.
    /// All other properties have their default values.</summary>
    /// <param name="endpoint">The connection endpoint.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ResumableClientConnection(
        Endpoint endpoint,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new ClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            Endpoint = endpoint
        })
    {
    }

    /// <summary>Establishes the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that provides the <see cref="TransportConnectionInformation"/> of the transport connection, once
    /// this connection is established. This task can also complete with one of the following exceptions:
    /// <list type="bullet">
    /// <item><description><see cref="ConnectionAbortedException"/>if the connection was aborted.</description></item>
    /// <item><description><see cref="ObjectDisposedException"/>if this connection is disposed.</description></item>
    /// <item><description><see cref="OperationCanceledException"/>if cancellation was requested through the
    /// cancellation token.</description></item>
    /// <item><description><see cref="TimeoutException"/>if this connection attempt or a previous attempt exceeded
    /// <see cref="ConnectionOptions.ConnectTimeout"/>.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ConnectionClosedException">Thrown if the connection was closed by this client.</exception>
    public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancel = default)
    {
        // make a copy of the client connection we're trying with
        ClientConnection clientConnection = _clientConnection;

        try
        {
            return await clientConnection.ConnectAsync(cancel).ConfigureAwait(false);
        }
        catch (ConnectionClosedException) when (IsResumable)
        {
            _ = RefreshClientConnectionAsync(clientConnection, graceful: true);

            // try again with the latest _clientConnection
            return await _clientConnection.ConnectAsync(cancel).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _clientConnection.DisposeAsync();

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        // make a copy of the client connection we're trying with
        ClientConnection clientConnection = _clientConnection;

        try
        {
            return await clientConnection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        catch (ConnectionClosedException) when (IsResumable)
        {
            _ = RefreshClientConnectionAsync(clientConnection, graceful: true);

            // try again with the latest _clientConnection
            return await _clientConnection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        // for retries on other exceptions, the application should use a retry interceptor
    }

    /// <summary>Adds a callback that will be executed when the underlying connection is aborted.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    public void OnAbort(Action<Exception> callback)
    {
        ClientConnection clientConnection;
        lock (_mutex)
        {
            _onAbort += callback; // for future connections
            clientConnection = _clientConnection;
        }

        clientConnection.OnAbort(callback);
    }

    /// <summary>Adds a callback that will be executed when the underlying connection is shut down.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    public void OnShutdown(Action<string> callback)
    {
        ClientConnection clientConnection;

        lock (_mutex)
        {
            _onShutdown += callback; // for future client connections
            clientConnection = _clientConnection;
        }

        clientConnection.OnShutdown(callback);
    }

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    public Task ShutdownAsync(CancellationToken cancel = default) =>
        ShutdownAsync("connection shutdown", cancel: cancel);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="message">The message transmitted to the server when using the IceRPC protocol.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    public Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            _isResumable = false;
        }
        return _clientConnection.ShutdownAsync(message, cancel);
    }

    private ClientConnection CreateClientConnection()
    {
        var clientConnection = new ClientConnection(
            _options,
            _loggerFactory,
            _multiplexedClientTransport,
            _duplexClientTransport);

        // only called from the constructor or with _mutex locked
        clientConnection.OnAbort(_onAbort + OnAbort);
        clientConnection.OnShutdown(_onShutdown + OnShutdown);

        void OnAbort(Exception exception) => _ = RefreshClientConnectionAsync(clientConnection, graceful: false);
        void OnShutdown(string message) => _ = RefreshClientConnectionAsync(clientConnection, graceful: true);

        return clientConnection;
    }

    private async Task RefreshClientConnectionAsync(ClientConnection clientConnection, bool graceful)
    {
        bool closeOldConnection = false;
        lock (_mutex)
        {
            // We only create a new connection and assign it to _clientConnection if it matches the clientConnection we
            // just tried. If it's another connection, another thread has already called RefreshClientConnection.
            if (_isResumable && clientConnection == _clientConnection)
            {
                _clientConnection = CreateClientConnection();
                closeOldConnection = true;
            }
        }

        if (closeOldConnection)
        {
            if (graceful)
            {
                try
                {
                    // Wait for existing graceful shutdown to complete, or fail immediately if aborted
                    await clientConnection.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // OnAbort will clean it up
                    return;
                }
            }

            await clientConnection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
