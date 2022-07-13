// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a protocol connection used to send and receive requests and responses. This protocol connection is
/// reconnected automatically when its underlying connection is closed by the server or the transport.</summary>
public sealed class ResumableClientConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the endpoint of this connection.</summary>
    // TODO: should we remove this property?
    public Endpoint Endpoint { get; }

    /// <summary>Gets the network connection information or <c>null</c> if the connection is not connected.
    /// </summary>
    public NetworkConnectionInformation? NetworkConnectionInformation
    {
        get
        {
            lock (_mutex)
            {
                return _networkConnectionInformation;
            }
        }

        private set
        {
            lock (_mutex)
            {
                _networkConnectionInformation = value;
            }
        }
    }

    /// <summary>Gets the protocol of this connection.</summary>
    public Protocol Protocol => Endpoint.Protocol;

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

    private bool _isDisposed;

    private bool _isResumable = true;

    private readonly ILoggerFactory? _loggerFactory;

    private readonly IClientTransport<IMultiplexedNetworkConnection> _multiplexedClientTransport;

    private readonly object _mutex = new();

    private NetworkConnectionInformation? _networkConnectionInformation;

    private Action<Exception>? _onAbort;

    private Action<string>? _onShutdown;

    private readonly ClientConnectionOptions _options;

    private IProtocolConnection _protocolConnection;

    private readonly IClientTransport<ISimpleNetworkConnection> _simpleClientTransport;

    /// <summary>Constructs a resumable protocol connection.</summary>
    /// <param name="options">The protocol connection options.</param>
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
        Endpoint = options.Endpoint ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.Endpoint)} is not set",
                nameof(options));

        _options = options;
        _loggerFactory = loggerFactory;
        _multiplexedClientTransport = multiplexedClientTransport ?? ClientConnection.DefaultMultiplexedClientTransport;
        _simpleClientTransport = simpleClientTransport ?? ClientConnection.DefaultSimpleClientTransport;

        _protocolConnection = CreateProtocolConnection();
    }

    /// <summary>Constructs a resumable protocol connection with the specified endpoint and client authentication options.
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
    /// <returns>A task that indicates the completion of the connect operation.</returns>
    /// <exception cref="ConnectionClosedException">Thrown if the connection was closed by this client.</exception>
    public async Task ConnectAsync(CancellationToken cancel = default)
    {
        CheckIfDisposed();

        // make a copy of the protocol connection we're trying with
        IProtocolConnection protocolConnection = _protocolConnection;

        try
        {
            NetworkConnectionInformation = await protocolConnection.ConnectAsync(cancel).ConfigureAwait(false);
            return;
        }
        catch (ConnectionClosedException) when (IsResumable)
        {
            _ = RefreshConnectionAsync(protocolConnection, graceful: true);

            // try again with the latest _protocolConnection
            NetworkConnectionInformation = await _protocolConnection.ConnectAsync(cancel).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _isDisposed = true;
        return _protocolConnection.DisposeAsync();
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        CheckIfDisposed();

        // make a copy of the protocol connection we're trying with
        IProtocolConnection protocolConnection = _protocolConnection;

        try
        {
            return await protocolConnection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        catch (ConnectionClosedException) when (IsResumable)
        {
            _ = RefreshConnectionAsync(protocolConnection, graceful: true);

            // try again with the latest _protocolConnection
            return await _protocolConnection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
        // for retries on other exceptions, the application should use a retry interceptor
    }

    /// <summary>Adds a callback that will be executed when the underlying connection is aborted.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    public void OnAbort(Action<Exception> callback)
    {
        IProtocolConnection protocolConnection;
        lock (_mutex)
        {
            _onAbort += callback; // for future connections
            protocolConnection = _protocolConnection;
        }

        protocolConnection.OnAbort(callback);
    }

    /// <summary>Adds a callback that will be executed when the underlying connection is shut down.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    public void OnShutdown(Action<string> callback)
    {
        IProtocolConnection protocolConnection;

        lock (_mutex)
        {
            _onShutdown += callback; // for future protocol connections
            protocolConnection = _protocolConnection;
        }

        protocolConnection.OnShutdown(callback);
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
        return _protocolConnection.ShutdownAsync(message, cancel);
    }

    private void CheckIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ResumableClientConnection));
        }
    }

    private IProtocolConnection CreateProtocolConnection()
    {
        IProtocolConnection protocolConnection = ProtocolConnection.CreateClientConnection(
            _options,
            _loggerFactory,
            _multiplexedClientTransport,
            _simpleClientTransport);

        // only called from the constructor or with _mutex locked
        protocolConnection.OnAbort(_onAbort + OnAbort);
        protocolConnection.OnShutdown(_onShutdown + OnShutdown);

        void OnAbort(Exception exception) => _ = RefreshConnectionAsync(protocolConnection, graceful: false);
        void OnShutdown(string message) => _ = RefreshConnectionAsync(protocolConnection, graceful: true);

        return protocolConnection;
    }

    private async Task RefreshConnectionAsync(IProtocolConnection protocolConnection, bool graceful)
    {
        bool closeOldConnection = false;
        lock (_mutex)
        {
            // We only create a new connection and assign it to _clientConnection if it matches the clientConnection we
            // just tried. If it's another connection, another thread has already called RefreshClientConnection.
            if (_isResumable && protocolConnection == _protocolConnection)
            {
                _protocolConnection = CreateProtocolConnection();
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
                    await protocolConnection.ShutdownAsync("", CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // OnAbort will clean it up
                    return;
                }
            }

            await protocolConnection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
