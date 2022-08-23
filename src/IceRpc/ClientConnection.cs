// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Transports;
using System.Collections.Immutable;
using System.Net.Security;

namespace IceRpc;

/// <summary>Represents a client connection used to send and receive requests and responses. This client connection is
/// reconnected automatically when its underlying connection is closed by the server or the transport.</summary>
public sealed class ClientConnection : IInvoker, IAsyncDisposable
{
    /// <summary>Gets the server address of this connection.</summary>
    /// <value>The server address of this connection. Its <see cref="ServerAddress.Transport"/> property is always
    /// non-null.</value>
    public ServerAddress ServerAddress => _connection.ServerAddress;

    private Task? _cleanupTask;

    private IProtocolConnection _connection;

    private readonly Func<IProtocolConnection> _connectionFactory;

    private bool _isResumable = true;

    private readonly object _mutex = new();

    private Action<Exception>? _onAbort;

    private Action<string>? _onShutdown;

    /// <summary>Constructs a client connection.</summary>
    /// <param name="options">The client connection options.</param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice connections.</param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc connections.</param>
    public ClientConnection(
        ClientConnectionOptions options,
        IDuplexClientTransport? duplexClientTransport = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null)
    {
        ServerAddress serverAddress = options.ServerAddress ??
            throw new ArgumentException(
                $"{nameof(ClientConnectionOptions.ServerAddress)} is not set",
                nameof(options));

        var clientProtocolConnectionFactory = new ClientProtocolConnectionFactory(
            options,
            options.ClientAuthenticationOptions,
            duplexClientTransport,
            multiplexedClientTransport);

        _connectionFactory = () =>
        {
            IProtocolConnection connection = clientProtocolConnectionFactory.CreateConnection(serverAddress);
            connection = new ConnectProtocolConnectionDecorator(connection);

            // only called from the constructor or with _mutex locked
            connection.OnAbort(_onAbort + OnAbort);
            connection.OnShutdown(_onShutdown + OnShutdown);

            void OnAbort(Exception exception) => RefreshConnection(connection, graceful: false);
            void OnShutdown(string message) => RefreshConnection(connection, graceful: true);

            return connection;
        };

        _connection = _connectionFactory();
    }

    /// <summary>Constructs a resumable client connection with the specified server address and client authentication
    /// options. All other properties have their default values.</summary>
    /// <param name="serverAddress">The connection server address.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ClientConnection(
        ServerAddress serverAddress,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new ClientConnectionOptions
        {
            ClientAuthenticationOptions = clientAuthenticationOptions,
            ServerAddress = serverAddress
        })
    {
    }

    /// <summary>Constructs a resumable client connection with the specified server address URI and client
    /// authentication options. All other properties have their default values.</summary>
    /// <param name="serverAddressUri">The connection server address URI.</param>
    /// <param name="clientAuthenticationOptions">The client authentication options.</param>
    public ClientConnection(
        Uri serverAddressUri,
        SslClientAuthenticationOptions? clientAuthenticationOptions = null)
        : this(new ServerAddress(serverAddressUri), clientAuthenticationOptions)
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
        // Keep a reference to the connection we're trying to connect to.
        IProtocolConnection connection = _connection;

        try
        {
            return await connection.ConnectAsync(cancel).ConfigureAwait(false);
        }
        catch (ConnectionClosedException)
        {
            if (RefreshConnection(connection, graceful: true) is IProtocolConnection newConnection)
            {
                // Try again once with the new connection
                return await newConnection.ConnectAsync(cancel).ConfigureAwait(false);
            }
            else
            {
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Task? cleanupTask;
        lock (_mutex)
        {
            _isResumable = false;
            cleanupTask = _cleanupTask;
        }

        if (cleanupTask is not null)
        {
            await Task.WhenAll(_connection.DisposeAsync().AsTask(), cleanupTask).ConfigureAwait(false);
        }
        else
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        if (request.Features.Get<IServerAddressFeature>() is IServerAddressFeature serverAddressFeature)
        {
            if (serverAddressFeature.ServerAddress is ServerAddress mainServerAddress)
            {
                CheckRequestServerAddresses(mainServerAddress, serverAddressFeature.AltServerAddresses);
            }
        }
        else if (request.ServiceAddress.ServerAddress is ServerAddress mainServerAddress)
        {
            CheckRequestServerAddresses(mainServerAddress, request.ServiceAddress.AltServerAddresses);
        }

        // If the request has no server address at all, we let it through.

        return PerformInvokeAsync();

        void CheckRequestServerAddresses(
            ServerAddress mainServerAddress,
            ImmutableList<ServerAddress> altServerAddresses)
        {
            if (ServerAddressComparer.OptionalTransport.Equals(mainServerAddress, ServerAddress))
            {
                return;
            }

            foreach (ServerAddress serverAddress in altServerAddresses)
            {
                if (ServerAddressComparer.OptionalTransport.Equals(serverAddress, ServerAddress))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                $"none of the server addresses of the request matches this connection's server address: {ServerAddress}");
        }

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            IProtocolConnection connection = _connection;

            try
            {
                return await connection.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
            catch (ConnectionClosedException)
            {
                if (RefreshConnection(connection, graceful: true) is IProtocolConnection newConnection)
                {
                    // try again once with the new connection
                    return await newConnection.InvokeAsync(request, cancel).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }
        }
    }

    /// <summary>Adds a callback that will be executed when the underlying connection is aborted.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    public void OnAbort(Action<Exception> callback)
    {
        IProtocolConnection connection;
        lock (_mutex)
        {
            _onAbort += callback; // for future connection created by _connectionFactory
            connection = _connection;
        }

        // call OnAbort on underlying connection outside mutex lock
        connection.OnAbort(callback);
    }

    /// <summary>Adds a callback that will be executed when the underlying connection is shut down by the peer or an
    /// idle timeout.</summary>
    /// <param name="callback">The callback to execute. It must not block or throw any exception.</param>
    public void OnShutdown(Action<string> callback)
    {
        IProtocolConnection connection;

        lock (_mutex)
        {
            _onShutdown += callback; // for future connection created by _connectionFactory
            connection = _connection;
        }

        // call OnShutdown on underlying connection outside mutex lock
        connection.OnShutdown(callback);
    }

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    public Task ShutdownAsync(CancellationToken cancel = default) =>
        ShutdownAsync("connection shutdown", cancel: cancel);

    /// <summary>Gracefully shuts down the connection.</summary>
    /// <param name="message">The message transmitted to the server when using the IceRPC protocol.</param>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    public Task ShutdownAsync(string message, CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            _isResumable = false;
        }
        return _connection.ShutdownAsync(message, cancel);
    }

    /// <summary>Refreshes _connection and returns the latest _connection, or null if ClientConnection is no longer
    /// resumable.</summary>
    private IProtocolConnection? RefreshConnection(IProtocolConnection connection, bool graceful)
    {
        IProtocolConnection? newConnection = null;

        lock (_mutex)
        {
            // We only create a new connection and assign it to _connection if it matches the connection we just tried.
            // If it's another connection, another thread has already called RefreshConnection.
            if (_isResumable)
            {
                if (connection == _connection)
                {
                    _connection = _connectionFactory();
                    _cleanupTask = PerformCleanupAsync(_cleanupTask);
                }
                newConnection = _connection;
            }
        }

        return newConnection;

        async Task PerformCleanupAsync(Task? previousTask)
        {
            await Task.Yield();

            if (graceful)
            {
                try
                {
                    // Wait for existing graceful shutdown to complete, or fail immediately if aborted.
                    await connection.ShutdownAsync("", CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }

            if (previousTask is not null)
            {
                await Task.WhenAll(previousTask, connection.DisposeAsync().AsTask()).ConfigureAwait(false);
            }
            else
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
