// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace IceRpc;

/// <summary>A connection cache is an invoker that routes outgoing requests to connections it manages. This routing is
/// based on the <see cref="IEndpointFeature"/> and the endpoints of the service address carried by each outgoing
/// request.</summary>
public sealed class ConnectionCache : IInvoker, IAsyncDisposable
{
    // Connected connections that can be returned immediately.
    private readonly Dictionary<Key, ClientConnection> _activeConnections = new();

    private bool _isReadOnly;

    private readonly ILoggerFactory? _loggerFactory;
    private readonly IMultiplexedClientTransport _multiplexedClientTransport;

    private readonly object _mutex = new();

    private readonly ConnectionCacheOptions _options;

    // New connections in the process of connecting. They can be returned only after ConnectAsync succeeds.
    private readonly Dictionary<Key, ClientConnection> _pendingConnections = new();

    // Formerly pending or active connections that are closed but not shutdown yet.
    private readonly HashSet<ClientConnection> _shutdownPendingConnections = new();

    private readonly IDuplexClientTransport _duplexClientTransport;

    /// <summary>Constructs a connection cache.</summary>
    /// <param name="options">The connection cache options.</param>
    /// <param name="loggerFactory">The logger factory used to create loggers to log connection-related activities.
    /// </param>
    /// <param name="multiplexedClientTransport">The multiplexed transport used to create icerpc protocol
    /// connections.</param>
    /// <param name="duplexClientTransport">The duplex transport used to create ice protocol connections.</param>
    public ConnectionCache(
        ConnectionCacheOptions options,
        ILoggerFactory? loggerFactory = null,
        IMultiplexedClientTransport? multiplexedClientTransport = null,
        IDuplexClientTransport? duplexClientTransport = null)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _multiplexedClientTransport = multiplexedClientTransport ?? ClientConnection.DefaultMultiplexedClientTransport;
        _duplexClientTransport = duplexClientTransport ?? ClientConnection.DefaultDuplexClientTransport;
    }

    /// <summary>Constructs a connection cache.</summary>
    /// <param name="clientConnectionOptions">The client connection options for connections created by this cache.
    /// </param>
    public ConnectionCache(ClientConnectionOptions clientConnectionOptions)
        : this(new ConnectionCacheOptions { ClientConnectionOptions = clientConnectionOptions })
    {
    }

    /// <summary>Releases all resources allocated by this connection cache.</summary>
    /// <returns>A value task that completes when all connections managed by this cache are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        lock (_mutex)
        {
            _isReadOnly = true;
        }

        // Dispose all connections managed by this cache.
        IEnumerable<ClientConnection> allConnections =
            _pendingConnections.Values.Concat(_activeConnections.Values).Concat(_shutdownPendingConnections);

        await Task.WhenAll(allConnections.Select(connection => connection.DisposeAsync().AsTask()))
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        if (request.Features.Get<IEndpointFeature>() is not IEndpointFeature endpointFeature)
        {
            endpointFeature = new EndpointFeature(request.ServiceAddress);
            request.Features = request.Features.With(endpointFeature);
        }

        if (endpointFeature.Endpoint is null)
        {
            throw new NoEndpointException(request.ServiceAddress);
        }

        return PerformInvokeAsync();

        async Task<IncomingResponse> PerformInvokeAsync()
        {
            ClientConnection connection = await GetClientConnectionAsync(endpointFeature, cancel).ConfigureAwait(false);
            return await connection.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
    }

    /// <summary>Gracefully shuts down all connections managed by this cache. This method can be called multiple times.
    /// </summary>
    /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A task that completes when the shutdown is complete.</returns>
    public Task ShutdownAsync(CancellationToken cancel = default)
    {
        lock (_mutex)
        {
            _isReadOnly = true;
        }

        // Shut down all connections managed by this cache.
        IEnumerable<ClientConnection> allConnections =
            _pendingConnections.Values.Concat(_activeConnections.Values).Concat(_shutdownPendingConnections);

        return Task.WhenAll(
            allConnections.Select(connection => connection.ShutdownAsync("connection cache shutdown", cancel)));
    }

    /// <summary>Checks with the protocol-dependent transport if this endpoint has valid parameters. We call this method
    /// when it appears we can reuse an active or pending connection based on a parameterless endpoint match.</summary>
    /// <param name="endpoint">The endpoint to check.</param>
    private void CheckEndpoint(Endpoint endpoint)
    {
        bool isValid = endpoint.Protocol == Protocol.Ice ?
            _duplexClientTransport.CheckParams(endpoint) :
            _multiplexedClientTransport.CheckParams(endpoint);

        if (!isValid)
        {
            throw new FormatException(
                $"cannot establish a client connection to endpoint '{endpoint}': one or more parameters are invalid");
        }
    }

    /// <summary>Creates a connection and attempts to connect this connection unless there is an active or pending
    /// connection for the desired endpoint.</summary>
    /// <param name="endpoint">The endpoint of the server.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A connected connection.</returns>
    private async ValueTask<ClientConnection> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
    {
        ClientConnection? connection = null;
        bool created = false;

        Key key = CreateKey(endpoint);

        lock (_mutex)
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException("connection cache shutting down");
            }

            if (_activeConnections.TryGetValue(key, out connection))
            {
                CheckEndpoint(endpoint);
                return connection;
            }
            else if (_pendingConnections.TryGetValue(key, out connection))
            {
                CheckEndpoint(endpoint);
                // and call ConnectAsync on this connection after the if block.
            }
            else
            {
                connection = new ClientConnection(
                    _options.ClientConnectionOptions with { Endpoint = endpoint },
                    _loggerFactory,
                    _multiplexedClientTransport,
                    _duplexClientTransport);

                created = true;
                _pendingConnections.Add(key, connection);
            }
        }

        if (created)
        {
            try
            {
                // TODO: add cancellation token to cancel when ConnectionCache is shut down / disposed.
                await connection.ConnectAsync(cancel).ConfigureAwait(false);
            }
            catch
            {
                bool scheduleRemoveFromClosed = false;

                lock (_mutex)
                {
                    // the _pendingConnections collection is read-only after shutdown
                    if (!_isReadOnly)
                    {
                        // "move" from pending to shutdown pending
                        bool removed = _pendingConnections.Remove(key);
                        Debug.Assert(removed);
                        _ = _shutdownPendingConnections.Add(connection);
                        scheduleRemoveFromClosed = true;
                    }
                }
                if (scheduleRemoveFromClosed)
                {
                    _ = RemoveFromClosedAsync(connection, graceful: false);
                }

                throw;
            }

            bool scheduleRemoveFromActive = false;

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    // "move" from pending to active
                    bool removed = _pendingConnections.Remove(key);
                    Debug.Assert(removed);
                    _activeConnections.Add(key, connection);
                    scheduleRemoveFromActive = true;
                }
                // this new connection is being shut down already
            }

            if (scheduleRemoveFromActive)
            {
                // Schedule removal after addition. We do this outside the mutex lock otherwise RemoveFromActive could
                // call await ShutdownAsync or DisposeAsync on the connection within this lock.
                connection.OnAbort(exception => RemoveFromActive(graceful: false));
                connection.OnShutdown(message => RemoveFromActive(graceful: true));
            }
        }
        else
        {
            await connection.ConnectAsync(cancel).ConfigureAwait(false);
        }

        return connection;

        void RemoveFromActive(bool graceful)
        {
            Debug.Assert(connection is not null);

            bool scheduleRemoveFromClosed = false;

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    // "move" from active to shutdown pending
                    bool removed = _activeConnections.Remove(CreateKey(connection.Endpoint));
                    Debug.Assert(removed);
                    _ = _shutdownPendingConnections.Add(connection);
                    scheduleRemoveFromClosed = true;
                }
            }

            if (scheduleRemoveFromClosed)
            {
                _ = RemoveFromClosedAsync(connection, graceful);
            }
        }

        // Remove connection from _shutdownPendingConnections once the dispose is complete
        async Task RemoveFromClosedAsync(ClientConnection clientConnection, bool graceful)
        {
            if (graceful)
            {
                // wait for current shutdown to complete
                try
                {
                    await clientConnection.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            await clientConnection.DisposeAsync().ConfigureAwait(false);

            lock (_mutex)
            {
                if (!_isReadOnly)
                {
                    _ = _shutdownPendingConnections.Remove(clientConnection);
                }
            }
        }
    }

    private Key CreateKey(Endpoint endpoint)
    {
        if (!endpoint.Params.TryGetValue("transport", out string? transportName))
        {
            transportName = endpoint.Protocol == Protocol.IceRpc ?
                _multiplexedClientTransport.Name : _duplexClientTransport.Name;
        }

        return new Key(endpoint, transportName);
    }

    /// <summary>Returns a client connection to one of the specified endpoints.</summary>
    /// <param name="endpointFeature">The endpoint feature.</param>
    /// <param name="cancel">The cancellation token.</param>
    private ValueTask<ClientConnection> GetClientConnectionAsync(
        IEndpointFeature endpointFeature,
        CancellationToken cancel)
    {
        ClientConnection? connection = null;
        Endpoint mainEndpoint = endpointFeature.Endpoint!.Value;

        if (_options.PreferExistingConnection)
        {
            lock (_mutex)
            {
                connection = GetActiveConnection(mainEndpoint);
                if (connection is null)
                {
                    for (int i = 0; i < endpointFeature.AltEndpoints.Count; ++i)
                    {
                        Endpoint altEndpoint = endpointFeature.AltEndpoints[i];
                        connection = GetActiveConnection(altEndpoint);
                        if (connection is not null)
                        {
                            // This altEndpoint becomes the main endpoint, and the existing main endpoint becomes
                            // the first alt endpoint.
                            endpointFeature.AltEndpoints = endpointFeature.AltEndpoints
                                .RemoveAt(i)
                                .Insert(0, mainEndpoint);
                            endpointFeature.Endpoint = altEndpoint;

                            break; // foreach
                        }
                    }
                }
            }
            if (connection is not null)
            {
                return new(connection);
            }
        }

        return GetOrCreateAsync();

        ClientConnection? GetActiveConnection(Endpoint endpoint)
        {
            if (_activeConnections.TryGetValue(CreateKey(endpoint), out ClientConnection? connection))
            {
                CheckEndpoint(endpoint);
                return connection;
            }
            else
            {
                return null;
            }
        }

        // Retrieve a pending connection and wait for its ConnectAsync to complete successfully, or create and connect
        // a brand new connection.
        async ValueTask<ClientConnection> GetOrCreateAsync()
        {
            try
            {
                return await ConnectAsync(mainEndpoint, cancel).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                List<Exception>? exceptionList = null;

                for (int i = 0; i < endpointFeature.AltEndpoints.Count; ++i)
                {
                    // Rotate the endpoints before each new connection attempt: the first alt endpoint becomes the main
                    // endpoint and the main endpoint becomes the last alt endpoint.
                    endpointFeature.Endpoint = endpointFeature.AltEndpoints[0];
                    endpointFeature.AltEndpoints = endpointFeature.AltEndpoints.RemoveAt(0).Add(mainEndpoint);
                    mainEndpoint = endpointFeature.Endpoint.Value;

                    try
                    {
                        return await ConnectAsync(mainEndpoint, cancel).ConfigureAwait(false);
                    }
                    catch (Exception altEx)
                    {
                        exceptionList ??= new List<Exception> { exception };
                        exceptionList.Add(altEx);
                        // and keep trying
                    }
                }

                if (exceptionList is null)
                {
                    throw;
                }
                else
                {
                    throw new AggregateException(exceptionList);
                }
            }
        }
    }

    // The key consists of the endpoint's protocol, host, port and transport.
    private readonly record struct Key
    {
        internal Endpoint Endpoint { get; }

        internal string TransportName { get; }

        public bool Equals(Key other) =>
            EndpointComparer.ParameterLess.Equals(Endpoint, other.Endpoint) &&
            TransportName == other.TransportName;

        public override int GetHashCode() =>
            HashCode.Combine(EndpointComparer.ParameterLess.GetHashCode(Endpoint), TransportName);

        internal Key(Endpoint endpoint, string transportName)
        {
            Endpoint = endpoint;
            TransportName = transportName;
        }
    }
}
