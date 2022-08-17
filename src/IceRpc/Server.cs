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
    /// <summary>Gets the default server transport for ice protocol connections.</summary>
    public static IDuplexServerTransport DefaultDuplexServerTransport { get; } = new TcpServerTransport();

    /// <summary>Gets the default server transport for icerpc protocol connections.</summary>
    public static IMultiplexedServerTransport DefaultMultiplexedServerTransport { get; } =
        new SlicServerTransport(new TcpServerTransport());

    /// <summary>Gets the server address of this server.</summary>
    /// <value>The server address of this server. Its <see cref="ServerAddress.Transport"/> property is always non-null.
    /// When the address's host is an IP address and the port is 0, <see cref="Listen"/> replaces the port by the actual
    /// port the server is listening on.</value>
    public ServerAddress ServerAddress => _listener?.ServerAddress ?? _serverAddress;

    /// <summary>Gets a task that completes when the server's shutdown is complete: see <see cref="ShutdownAsync"/>.
    /// This property can be retrieved before shutdown is initiated.</summary>
    public Task ShutdownComplete => _shutdownCompleteSource.Task;

    private readonly Dictionary<ProtocolConnection, EndPoint> _connections = new();

    private bool _isReadOnly;

    private IListener? _listener;

    private readonly Func<IListener> _listenerFactory;

    // protects _listener, _connections and _isReadOnly
    private readonly object _mutex = new();

    private readonly ServerAddress _serverAddress;

    private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Constructs a server.</summary>
    /// <param name="options">The server options.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections.</param>
    public Server(
        ServerOptions options,
        IMultiplexedServerTransport? multiplexedServerTransport = null,
        IDuplexServerTransport? duplexServerTransport = null)
    {
        if (options.ConnectionOptions.Dispatcher is null)
        {
            throw new ArgumentException(
                $"{nameof(ServerOptions.ConnectionOptions.Dispatcher)} cannot be null");
        }

        _serverAddress = options.ServerAddress;
        duplexServerTransport ??= DefaultDuplexServerTransport;
        multiplexedServerTransport ??= DefaultMultiplexedServerTransport;

        if (_serverAddress.Transport is null)
        {
            _serverAddress = ServerAddress with
            {
                Transport = _serverAddress.Protocol == Protocol.Ice ?
                    duplexServerTransport.Name : multiplexedServerTransport.Name
            };
        }

        _listenerFactory = () =>
        {
            if (_serverAddress.Protocol == Protocol.Ice)
            {
                IListener<IDuplexConnection> listener = duplexServerTransport.Listen(
                    _serverAddress,
                    new DuplexConnectionOptions
                    {
                        MinSegmentSize = options.ConnectionOptions.MinSegmentSize,
                        Pool = options.ConnectionOptions.Pool,
                    },
                    options.ServerAuthenticationOptions);

                // Run task to start accepting new connections
                _ = Task.Run(() => AcceptAsync(() => listener.AcceptAsync(), CreateProtocolConnection));

                return listener;

                ProtocolConnection CreateProtocolConnection(IDuplexConnection duplexConnection) =>
                    new IceProtocolConnection(duplexConnection, isServer: true, options.ConnectionOptions);
            }
            else
            {
                IListener<IMultiplexedConnection> listener = multiplexedServerTransport.Listen(
                    _serverAddress,
                    new MultiplexedConnectionOptions
                    {
                        MaxBidirectionalStreams = options.ConnectionOptions.MaxIceRpcBidirectionalStreams,
                        // Add an additional stream for the icerpc protocol control stream.
                        MaxUnidirectionalStreams = options.ConnectionOptions.MaxIceRpcUnidirectionalStreams + 1,
                        MinSegmentSize = options.ConnectionOptions.MinSegmentSize,
                        Pool = options.ConnectionOptions.Pool,
                        StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter
                    },
                    options.ServerAuthenticationOptions);
                _listener = listener;

                // Run task to start accepting new connections
                _ = Task.Run(() => AcceptAsync(() => listener.AcceptAsync(), CreateProtocolConnection));

                return listener;

                ProtocolConnection CreateProtocolConnection(IMultiplexedConnection multiplexedConnection) =>
                    new IceRpcProtocolConnection(multiplexedConnection, options.ConnectionOptions);
            }

            async Task AcceptAsync<T>(
                Func<Task<(T, EndPoint)>> acceptTransportConnection,
                Func<T, ProtocolConnection> createProtocolConnection)
            {
                while (true)
                {
                    T transportConnection;
                    EndPoint remoteNetworkAddress;
                    try
                    {
                        (transportConnection, remoteNetworkAddress) = await acceptTransportConnection()
                            .ConfigureAwait(false);
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

                    ServerEventSource.Log.ConnectionStart(ServerAddress, remoteNetworkAddress);
                    ProtocolConnection connection = createProtocolConnection(transportConnection); // does not throw

                    bool done = false;
                    lock (_mutex)
                    {
                        if (_isReadOnly)
                        {
                            done = true;
                        }
                        else
                        {
                            _ = _connections[connection] = remoteNetworkAddress;
                        }
                    }

                    if (done)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                        ServerEventSource.Log.ConnectionStop(ServerAddress, remoteNetworkAddress);
                        return;
                    }

                    // Schedule removal after addition. We do this outside the mutex lock otherwise
                    // await serverConnection.ShutdownAsync could be called within this lock.
                    connection.OnAbort(exception =>
                        {
                            ServerEventSource.Log.ConnectionFailure(
                                ServerAddress,
                                remoteNetworkAddress,
                                exception);
                            _ = RemoveFromCollectionAsync(connection, graceful: false, remoteNetworkAddress);
                        });

                    connection.OnShutdown(
                        message => _ = RemoveFromCollectionAsync(connection, graceful: true, remoteNetworkAddress));

                    // We don't wait for the connection to be activated. This could take a while for some transports
                    // such as TLS based transports where the handshake requires few round trips between the client and
                    // server.
                    // Waiting could also cause a security issue if the client doesn't respond to the connection
                    // initialization as we wouldn't be able to accept new connections in the meantime. The call will
                    // eventually timeout if the ConnectTimeout expires.

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            ServerEventSource.Log.ConnectStart(ServerAddress, remoteNetworkAddress);
                            await connection.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
                            ServerEventSource.Log.ConnectSuccess(ServerAddress, remoteNetworkAddress);
                        }
                        catch (Exception exception)
                        {
                            ServerEventSource.Log.ConnectFailure(ServerAddress, remoteNetworkAddress, exception);
                        }
                        finally
                        {
                            ServerEventSource.Log.ConnectStop(ServerAddress, remoteNetworkAddress);
                        }
                    });
                }
            }

            // Remove the connection from _connections once shutdown completes
            async Task RemoveFromCollectionAsync(
                ProtocolConnection connection,
                bool graceful,
                EndPoint remoteNetworkAddress)
            {
                lock (_mutex)
                {
                    if (_isReadOnly)
                    {
                        return; // already shutting down / disposed / being disposed by another thread
                    }
                }

                if (graceful)
                {
                    // Wait for the current shutdown to complete
                    try
                    {
                        await connection.ShutdownAsync("", CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        ServerEventSource.Log.ConnectionShutdownFailure(ServerAddress, remoteNetworkAddress, exception);
                    }
                }

                await connection.DisposeAsync().ConfigureAwait(false);

                bool removed = false;

                lock (_mutex)
                {
                    // the _connections collection is read-only when shutting down or disposing.
                    if (!_isReadOnly)
                    {
                        removed = _connections.Remove(connection);
                    }
                }

                if (removed)
                {
                    ServerEventSource.Log.ConnectionStop(ServerAddress, remoteNetworkAddress);
                }
            }
        };
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
        bool logConnectionStop = false;

        lock (_mutex)
        {
            _isReadOnly = true;

            if (_listener is not null)
            {
                // Stop accepting new connections by disposing of the listener
                _listener.Dispose();
                _listener = null;
                logConnectionStop = true;
            }
        }

        await Task.WhenAll(
            _connections.Select(
                async (entry) =>
                {
                    // Several threads can call DisposeAsync on the same connection; we wait for this DisposeAsync to
                    // complete.
                    await entry.Key.DisposeAsync().ConfigureAwait(false);

                    if (logConnectionStop)
                    {
                        // We only call ConnectionStop when we dispose the listener to avoid double-counting. If the
                        // listener was never created, _connections is empty.
                        ServerEventSource.Log.ConnectionStop(entry.Key.ServerAddress, entry.Value);
                    }
                })).ConfigureAwait(false);

        _ = _shutdownCompleteSource.TrySetResult(null);
    }

    /// <summary>Starts listening on the configured server address and dispatching requests from clients.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the server is already listening, shut down or
    /// shutting down.</exception>
    /// <exception cref="TransportException">Thrown when another server is already listening on the same server address.
    /// </exception>
    public void Listen()
    {
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

            _listener = _listenerFactory();
        }
    }

    /// <summary>Shuts down this server: the server stops accepting new connections and shuts down gracefully all its
    /// existing connections.</summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes once the shutdown is complete.</returns>
    public async Task ShutdownAsync(CancellationToken cancel = default)
    {
        try
        {
            lock (_mutex)
            {
                _isReadOnly = true;

                // Stop accepting new connections by disposing of the listener.
                _listener?.Dispose();
            }

            await Task.WhenAll(
                _connections.Select(
                    async (entry) =>
                    {
                        try
                        {
                            await entry.Key.ShutdownAsync("server shutdown", cancel).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            ServerEventSource.Log.ConnectionShutdownFailure(
                                entry.Key.ServerAddress,
                                entry.Value,
                                exception);
                        }
                    })).ConfigureAwait(false);
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
}
