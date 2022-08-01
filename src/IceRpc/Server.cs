// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc;

/// <summary>A server serves clients by listening for the requests they send, processing these requests and sending
/// the corresponding responses.</summary>
public sealed class Server : IAsyncDisposable
{
    /// <summary>Gets the default server transport for ice protocol connections.</summary>
    public static IDuplexServerTransport DefaultDuplexServerTransport { get; } = new TcpServerTransport();

    /// <summary>Gets the default server transport for icerpc protocol connections.</summary>
    public static IMultiplexedServerTransport DefaultMultiplexedServerTransport { get; } =
        new SlicServerTransport(new TcpServerTransport());

    /// <summary>Gets the endpoint of this server.</summary>
    /// <value>The endpoint of this server. Once <see cref="Listen"/> is called, the endpoint's value is the
    /// listening endpoint returned by the transport. It has a non-null Transport property even when
    /// <see cref="ServerOptions.Endpoint"/> does not.</value>
    public Endpoint Endpoint { get; private set; }

    /// <summary>Gets a task that completes when the server's shutdown is complete: see <see
    /// cref="ShutdownAsync"/>. This property can be retrieved before shutdown is initiated.</summary>
    public Task ShutdownComplete => _shutdownCompleteSource.Task;

    private readonly HashSet<IProtocolConnection> _connections = new();

    private bool _isReadOnly;

    private IDisposable? _listener;

    private readonly Func<IDisposable> _listenerFactory;

    // protects _shutdownTask
    private readonly object _mutex = new();

    private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Constructs a server.</summary>
    /// <param name="options">The server options.</param>
    /// <param name="loggerFactory">The logger factory used to create the IceRpc logger.</param>
    /// <param name="multiplexedServerTransport">The transport used to create icerpc protocol connections.</param>
    /// <param name="duplexServerTransport">The transport used to create ice protocol connections.</param>
    public Server(
        ServerOptions options,
        ILoggerFactory? loggerFactory = null,
        IMultiplexedServerTransport? multiplexedServerTransport = null,
        IDuplexServerTransport? duplexServerTransport = null)
    {
        if (options.ConnectionOptions.Dispatcher is null)
        {
            throw new ArgumentException(
                $"{nameof(ServerOptions.ConnectionOptions.Dispatcher)} cannot be null");
        }

        Endpoint = options.Endpoint;
        duplexServerTransport ??= DefaultDuplexServerTransport;
        multiplexedServerTransport ??= DefaultMultiplexedServerTransport;

        loggerFactory ??= NullLoggerFactory.Instance;
        ILogger logger = loggerFactory.CreateLogger(GetType().FullName!);

        if (logger != NullLogger.Instance)
        {
            duplexServerTransport = new LogDuplexServerTransportDecorator(duplexServerTransport, logger);
            multiplexedServerTransport = new LogMultiplexedServerTransportDecorator(multiplexedServerTransport, logger);

            if (options.ConnectionOptions.Dispatcher is IDispatcher dispatcher)
            {
                options = options with
                {
                    ConnectionOptions = options.ConnectionOptions with
                    {
                        Dispatcher = new LogDispatcherDecorator(dispatcher, logger)
                    }
                };
            }
        }

        _listenerFactory = CreateListener;

        IDisposable CreateListener()
        {
            if (options.Endpoint.Protocol == Protocol.Ice)
            {
                var duplexListenerOptions = new DuplexListenerOptions
                {
                    ServerConnectionOptions = new()
                    {
                        MinSegmentSize = options.ConnectionOptions.MinSegmentSize,
                        Pool = options.ConnectionOptions.Pool,
                        ServerAuthenticationOptions = options.ServerAuthenticationOptions
                    },
                    Endpoint = options.Endpoint
                };

                IDuplexListener listener = duplexServerTransport.Listen(duplexListenerOptions);
                Endpoint = listener.Endpoint;

                // Run task to start accepting new connections
                _ = Task.Run(() => AcceptAsync(
                    () => listener.AcceptAsync(),
                    logger == NullLogger.Instance ? CreateProtocolConnection : CreateProtocolConnectionWithLogger));

                return listener;

                ProtocolConnection CreateProtocolConnection(IDuplexConnection duplexConnection) =>
                    new IceProtocolConnection(duplexConnection, isServer: true, options.ConnectionOptions);

                IProtocolConnection CreateProtocolConnectionWithLogger(IDuplexConnection duplexConnection)
                {
                    ProtocolConnection decoratee = CreateProtocolConnection(duplexConnection);

                    IProtocolConnection decorator = new LogProtocolConnectionDecorator(decoratee, logger);
                    decoratee.Decorator = decorator;
                    return decorator;
                }
            }
            else
            {
                var multiplexedListenerOptions = new MultiplexedListenerOptions
                {
                    ServerConnectionOptions = new()
                    {
                        MaxBidirectionalStreams = options.ConnectionOptions.MaxIceRpcBidirectionalStreams,
                        // Add an additional stream for the icerpc protocol control stream.
                        MaxUnidirectionalStreams = options.ConnectionOptions.MaxIceRpcUnidirectionalStreams + 1,
                        MinSegmentSize = options.ConnectionOptions.MinSegmentSize,
                        Pool = options.ConnectionOptions.Pool,
                        ServerAuthenticationOptions = options.ServerAuthenticationOptions,
                        StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter
                    },
                    Endpoint = options.Endpoint
                };

                IMultiplexedListener listener = multiplexedServerTransport.Listen(multiplexedListenerOptions);
                Endpoint = listener.Endpoint;
                _listener = listener;

                // Run task to start accepting new connections
                _ = Task.Run(() => AcceptAsync(
                    () => listener.AcceptAsync(),
                    logger == NullLogger.Instance ? CreateProtocolConnection : CreateProtocolConnectionWithLogger));

                return listener;

                ProtocolConnection CreateProtocolConnection(IMultiplexedConnection multiplexedConnection) =>
                    new IceRpcProtocolConnection(multiplexedConnection, options.ConnectionOptions);

                // TODO: reduce duplication with Duplex code above
                IProtocolConnection CreateProtocolConnectionWithLogger(IMultiplexedConnection multiplexedConnection)
                {
                    ProtocolConnection decoratee = CreateProtocolConnection(multiplexedConnection);

                    IProtocolConnection decorator = new LogProtocolConnectionDecorator(decoratee, logger);
                    decoratee.Decorator = decorator;

                    return decorator;
                }
            }

            async Task AcceptAsync<T>(
                Func<Task<T>> acceptTransportConnection,
                Func<T, IProtocolConnection> createProtocolConnection)
            {
                while (true)
                {
                    IProtocolConnection connection;
                    try
                    {
                        connection = createProtocolConnection(await acceptTransportConnection().ConfigureAwait(false));
                    }
                    catch
                    {
                        lock (_mutex)
                        {
                            if (_isReadOnly)
                            {
                                return;
                            }
                        }

                        // We wait for one second to avoid running in a tight loop in case the failures occurs
                        // immediately again. Failures here are unexpected and could be considered fatal.
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                        continue;
                    }

                    lock (_mutex)
                    {
                        if (_isReadOnly)
                        {
                            connection.DisposeAsync().AsTask();
                            return;
                        }
                        _ = _connections.Add(connection);
                    }

                    // Schedule removal after addition. We do this outside the mutex lock otherwise
                    // await serverConnection.ShutdownAsync could be called within this lock.
                    connection.OnAbort(exception => _ = RemoveFromCollectionAsync(connection, graceful: false));
                    connection.OnShutdown(message => _ = RemoveFromCollectionAsync(connection, graceful: true));

                    // We don't wait for the connection to be activated. This could take a while for some transports
                    // such as TLS based transports where the handshake requires few round trips between the client and
                    // server.
                    // Waiting could also cause a security issue if the client doesn't respond to the connection
                    // initialization as we wouldn't be able to accept new connections in the meantime. The call will
                    // eventually timeout if the ConnectTimeout expires.
                    _ = connection.ConnectAsync(CancellationToken.None);
                }
            }

            // Remove the connection from _connections once shutdown completes
            async Task RemoveFromCollectionAsync(IProtocolConnection connection, bool graceful)
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
                    catch
                    {
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

    /// <summary>Constructs a server with the specified dispatcher, endpoint and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="endpoint">The endpoint of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    /// <param name="loggerFactory">The logger factory used to create the IceRpc logger.</param>
    public Server(
        IDispatcher dispatcher,
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions = null,
        ILoggerFactory? loggerFactory = null)
        : this(
            new ServerOptions
            {
                ServerAuthenticationOptions = authenticationOptions,
                ConnectionOptions = new()
                {
                    Dispatcher = dispatcher,
                },
                Endpoint = endpoint
            },
            loggerFactory)
    {
    }

    /// <summary>Constructs a server with the specified dispatcher, endpoint URI and authentication options. All other
    /// properties have their default values.</summary>
    /// <param name="dispatcher">The dispatcher of the server.</param>
    /// <param name="endpointUri">A URI that represents the endpoint of the server.</param>
    /// <param name="authenticationOptions">The server authentication options.</param>
    /// <param name="loggerFactory">The logger factory used to create the IceRpc logger.</param>
    public Server(
        IDispatcher dispatcher,
        Uri endpointUri,
        SslServerAuthenticationOptions? authenticationOptions = null,
        ILoggerFactory? loggerFactory = null)
        : this(dispatcher, new Endpoint(endpointUri), authenticationOptions, loggerFactory)
    {
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            _isReadOnly = true;

            // Stop accepting new connections by disposing of the listener
            _listener?.Dispose();
            _listener = null;
        }

        await Task.WhenAll(_connections.Select(connection => connection.DisposeAsync().AsTask()))
            .ConfigureAwait(false);

        _ = _shutdownCompleteSource.TrySetResult(null);
    }

    /// <summary>Starts listening on the configured endpoint and dispatching requests from clients. If the
    /// configured endpoint is an IP endpoint with port 0, this method updates the endpoint to include the actual
    /// port selected by the operating system.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the server is already listening.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the server is shut down or shutting down.</exception>
    /// <exception cref="TransportException">Thrown when another server is already listening on the same endpoint.
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
                _listener = null;
            }

            await Task.WhenAll(_connections.Select(connection => connection.ShutdownAsync("server shutdown", cancel)))
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
    public override string ToString() => Endpoint.ToString();
}
