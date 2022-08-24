// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System.Net;
using System.Net.Security;

namespace IceRpc;

/// <summary>A server serves clients by listening for the requests they send, processing these requests and sending the
/// corresponding responses.</summary>
public sealed class Server : IAsyncDisposable
{
    /// <summary>Gets the server address of this server.</summary>
    /// <value>The server address of this server. Its <see cref="ServerAddress.Transport"/> property is always non-null.
    /// When the address's host is an IP address and the port is 0, <see cref="Listen"/> replaces the port by the actual
    /// port the server is listening on.</value>
    public ServerAddress ServerAddress => _listener?.ServerAddress ?? _serverAddress;

    /// <summary>Gets a task that completes when the server's shutdown is complete: see <see cref="ShutdownAsync"/>.
    /// This property can be retrieved before shutdown is initiated.</summary>
    public Task ShutdownComplete => _shutdownCompleteSource.Task;

    private readonly HashSet<IProtocolConnection> _connections = new();

    private bool _isReadOnly;

    private IListener? _listener;

    private readonly Func<IListener<IProtocolConnection>> _listenerFactory;

    // protects _listener, _connections and _isReadOnly
    private readonly object _mutex = new();

    private readonly ServerAddress _serverAddress;

    private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Constructs a server.</summary>
    /// <param name="options">The server options.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections. Null is equivalent
    /// to <see cref="IDuplexServerTransport.Default"/>.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections. Null is
    /// equivalent to <see cref="IMultiplexedServerTransport.Default"/>.</param>
    public Server(
        ServerOptions options,
        IDuplexServerTransport? duplexServerTransport = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null)
    {
        if (options.ConnectionOptions.Dispatcher is null)
        {
            throw new ArgumentException(
                $"{nameof(ServerOptions.ConnectionOptions.Dispatcher)} cannot be null");
        }

        _serverAddress = options.ServerAddress;
        duplexServerTransport ??= IDuplexServerTransport.Default;
        multiplexedServerTransport ??= IMultiplexedServerTransport.Default;

        if (_serverAddress.Transport is null)
        {
            _serverAddress = ServerAddress with
            {
                Transport = _serverAddress.Protocol == Protocol.Ice ?
                    duplexServerTransport.Name : multiplexedServerTransport.Name
            };
        }

        _listenerFactory = () => new LogListenerDecorator(
            _serverAddress.Protocol == Protocol.Ice ?
                new IceProtocolListener(_serverAddress, options, duplexServerTransport) :
                new IceRpcProtocolListener(_serverAddress, options, multiplexedServerTransport));
    }

    /// <summary>Constructs a server with the specified dispatcher and authentication options. All other properties
    /// have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    public Server(IDispatcher dispatcher, SslServerAuthenticationOptions? authenticationOptions = null)
        : this(new ServerOptions
        {
            ServerAuthenticationOptions = authenticationOptions,
            ConnectionOptions = new()
            {
                Dispatcher = dispatcher,
            }
        })
    {
    }

    /// <summary>Constructs a server with the specified dispatcher, server address and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddress">The server address of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    public Server(
        IDispatcher dispatcher,
        ServerAddress serverAddress,
        SslServerAuthenticationOptions? authenticationOptions = null)
        : this(
            new ServerOptions
            {
                ServerAuthenticationOptions = authenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                },
                ServerAddress = serverAddress
            })
    {
    }

    /// <summary>Constructs a server with the specified dispatcher, server address URI and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="serverAddressUri">A URI that represents the server address of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    public Server(
        IDispatcher dispatcher,
        Uri serverAddressUri,
        SslServerAuthenticationOptions? authenticationOptions = null)
        : this(dispatcher, new ServerAddress(serverAddressUri), authenticationOptions)
    {
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            _isReadOnly = true;

            if (_listener is not null)
            {
                // Stop accepting new connections by disposing of the listener
                _listener.Dispose();
                _listener = null;
            }
        }

        await Task.WhenAll(_connections.Select(entry => entry.DisposeAsync().AsTask())).ConfigureAwait(false);
        _ = _shutdownCompleteSource.TrySetResult(null);
    }

    /// <summary>Starts listening on the configured server address and dispatching requests from clients.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the server is already listening, shut down or
    /// shutting down.</exception>
    /// <exception cref="TransportException">Thrown when another server is already listening on the same server address.
    /// </exception>
    public void Listen()
    {
        IListener<IProtocolConnection> listener;

        // We lock the mutex because ShutdownAsync can run concurrently.
        lock (_mutex)
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException($"server '{this}' is shut down or shutting down");
            }
            if (_listener is not null)
            {
                throw new InvalidOperationException($"server '{this}' is already listening");
            }

            listener = _listenerFactory();
            _listener = listener;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                IProtocolConnection connection;
                try
                {
                    (connection, _) = await listener.AcceptAsync().ConfigureAwait(false);
                }
                catch
                {
                    lock (_mutex)
                    {
                        if (_isReadOnly)
                        {
                            return; // server is shutting down or being disposed
                        }
                    }

                    // We wait for one second to avoid running in a tight loop in case the failures occurs
                    // immediately again. Failures here are unexpected and could be considered fatal.
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    continue;
                }

                bool done = false;
                lock (_mutex)
                {
                    if (_isReadOnly)
                    {
                        done = true;
                    }
                    else
                    {
                        _ = _connections.Add(connection);
                    }
                }

                if (done)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    return;
                }

                // Schedule removal after addition. We do this outside the mutex lock otherwise
                // await serverConnection.ShutdownAsync could be called within this lock.
                connection.OnAbort(
                    exception => _ = RemoveFromCollectionAsync(connection, shutdownMessage: null));

                connection.OnShutdown(
                    shutdownMessage => _ = RemoveFromCollectionAsync(connection, shutdownMessage));

                // We don't wait for the connection to be activated. This could take a while for some transports
                // such as TLS based transports where the handshake requires few round trips between the client and
                // server.
                // Waiting could also cause a security issue if the client doesn't respond to the connection
                // initialization as we wouldn't be able to accept new connections in the meantime. The call will
                // eventually timeout if the ConnectTimeout expires.
                _ = Task.Run(() => _ = connection.ConnectAsync(CancellationToken.None));
            }
        });

        // Remove the connection from _connections once shutdown completes
        async Task RemoveFromCollectionAsync(IProtocolConnection connection, string? shutdownMessage)
        {
            lock (_mutex)
            {
                if (_isReadOnly)
                {
                    return; // already shutting down / disposed / being disposed by another thread
                }
            }

            if (shutdownMessage is not null)
            {
                // Wait for the current shutdown to complete.
                // We pass shutdownMessage to ShutdownAsync to log this message since this shutdown was initiated from
                // the IceRPC internals and did not call the decorated connection.
                try
                {
                    await connection.ShutdownAsync(shutdownMessage, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }
            }

            await connection.DisposeAsync().ConfigureAwait(false);

            lock (_mutex)
            {
                // the _connections collection is read-only when shutting down or disposing.
                if (!_isReadOnly)
                {
                    _ = _connections.Remove(connection);
                }
            }
        }
    }

    /// <summary>Shuts down this server: the server stops accepting new connections and shuts down gracefully all its
    /// existing connections.</summary>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_mutex)
            {
                _isReadOnly = true;

                // Stop accepting new connections by disposing of the listener.
                _listener?.Dispose();
            }

            await Task.WhenAll(_connections.Select(entry => entry.ShutdownAsync("server shutdown", cancellationToken)))
                .ConfigureAwait(false);
        }
        finally
        {
            // The continuation is executed asynchronously (see _shutdownCompleteSource's construction). This
            // way, even if the continuation blocks waiting on ShutdownAsync to complete (with incorrect code
            // using Result or Wait()), ShutdownAsync will complete.
            _ = _shutdownCompleteSource.TrySetResult(null);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => ServerAddress.ToString();

    /// <summary>Provides a decorator that adds logging to a <see cref="IListener{T}"/> of
    /// <see cref="IProtocolConnection"/>.</summary>
    private class LogListenerDecorator : IListener<IProtocolConnection>
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IListener<IProtocolConnection> _decoratee;

        public async Task<(IProtocolConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync()
        {
            (IProtocolConnection connection, EndPoint remoteNetworkAddress) = await _decoratee.AcceptAsync()
                .ConfigureAwait(false);

            // We don't log AcceptAsync exceptions; they usually occur when the server is shutting down.

            ServerEventSource.Log.ConnectionStart(ServerAddress, remoteNetworkAddress);

            connection.OnAbort(exception =>
                ServerEventSource.Log.ConnectionFailure(
                    ServerAddress,
                    remoteNetworkAddress,
                    exception));

            return (new LogProtocolConnectionDecorator(connection, remoteNetworkAddress), remoteNetworkAddress);
        }

        public void Dispose() => _decoratee.Dispose();

        internal LogListenerDecorator(IListener<IProtocolConnection> decoratee) => _decoratee = decoratee;
    }

    /// <summary>Provides a decorator that adds EventSource-based logging to the <see cref="IProtocolConnection"/>.
    /// </summary>
    private class LogProtocolConnectionDecorator : IProtocolConnection
    {
        public ServerAddress ServerAddress => _decoratee.ServerAddress;

        private readonly IProtocolConnection _decoratee;
        private readonly EndPoint _remoteNetworkAddress;

        public async Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken)
        {
            ServerEventSource.Log.ConnectStart(ServerAddress, _remoteNetworkAddress);
            try
            {
                TransportConnectionInformation result = await _decoratee.ConnectAsync(cancellationToken).ConfigureAwait(false);
                ServerEventSource.Log.ConnectSuccess(ServerAddress, _remoteNetworkAddress);
                return result;
            }
            catch (Exception exception)
            {
                ServerEventSource.Log.ConnectFailure(ServerAddress, _remoteNetworkAddress, exception);
                throw;
            }
            finally
            {
                ServerEventSource.Log.ConnectStop(ServerAddress, _remoteNetworkAddress);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _decoratee.DisposeAsync().ConfigureAwait(false);
            ServerEventSource.Log.ConnectionStop(ServerAddress, _remoteNetworkAddress);
        }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken) =>
            _decoratee.InvokeAsync(request, cancellationToken);

        public void OnAbort(Action<Exception> callback) => _decoratee.OnAbort(callback);

        public void OnShutdown(Action<string> callback) => _decoratee.OnShutdown(callback);

        public async Task ShutdownAsync(string message, CancellationToken cancellationToken = default)
        {
            // TODO: we should log the shutdown message!

            try
            {
                await _decoratee.ShutdownAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                ServerEventSource.Log.ConnectionShutdownFailure(ServerAddress, _remoteNetworkAddress, exception);
                throw;
            }
        }

        internal LogProtocolConnectionDecorator(IProtocolConnection decoratee, EndPoint remoteNetworkAddress)
        {
            _decoratee = decoratee;
            _remoteNetworkAddress = remoteNetworkAddress;
        }
    }
}
