// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    internal abstract class IncomingConnectionFactory
    {
        internal abstract Endpoint Endpoint { get; }
        internal abstract void Activate();

        internal bool IsLocal(Endpoint endpoint) => endpoint.IsLocal(Endpoint);

        internal abstract Task ShutdownAsync();
    }

    // IncomingConnectionFactory for acceptor based transports.
    internal sealed class AcceptorIncomingConnectionFactory : IncomingConnectionFactory
    {
        internal override Endpoint Endpoint { get; }

        private readonly IAcceptor _acceptor;
        private Task? _acceptTask;
        private readonly Server _server;
        private readonly Communicator _communicator;
        private readonly HashSet<Connection> _connections = new();
        private readonly object _mutex = new();
        private bool _shutdown;

        public override string ToString() => _acceptor.ToString()!;

        internal AcceptorIncomingConnectionFactory(Server server, Endpoint endpoint)
        {
            _communicator = server.Communicator;
            _server = server;
            _acceptor = endpoint.Acceptor(_server);
            Endpoint = _acceptor.Endpoint;

            if (server.Logger.IsEnabled(LogLevel.Debug) && !(_acceptor is ColocatedAcceptor))
            {
                server.Logger.LogAcceptingConnections(Endpoint.Transport, _acceptor);
            }
        }

        internal override void Activate()
        {
            if (_server.Logger.IsEnabled(LogLevel.Information))
            {
                _server.Logger.LogStartAcceptingConnections(Endpoint.Transport, _acceptor);
            }

            // Start the asynchronous operation from the thread pool to prevent eventually accepting
            // synchronously new connections from this thread.
            lock (_mutex)
            {
                Debug.Assert(!_shutdown);
                _acceptTask = Task.Factory.StartNew(AcceptAsync,
                                                    default,
                                                    TaskCreationOptions.None,
                                                    _server.TaskScheduler ?? TaskScheduler.Default);
            }
        }

        internal void Remove(Connection connection)
        {
            lock (_mutex)
            {
                if (!_shutdown)
                {
                    _connections.Remove(connection);
                }
            }
        }

        internal override async Task ShutdownAsync()
        {
            if (_communicator.Logger.IsEnabled(LogLevel.Information))
            {
                _communicator.Logger.LogStopAcceptingConnections(Endpoint.Transport, _acceptor);
            }

            // Dispose of the acceptor and close the connections. It's important to perform this synchronously without
            // any await in between to guarantee that once Communicator.ShutdownAsync returns the communicator no
            // longer accepts any requests.

            lock (_mutex)
            {
                _shutdown = true;
                _acceptor.Dispose();
            }

            // The connection set is immutable once _shutdown is true
            var exception = new ObjectDisposedException($"{typeof(Server).FullName}:{_server.Name}");
            IEnumerable<Task> tasks = _connections.Select(connection => connection.GoAwayAsync(exception));

            // Wait for AcceptAsync and the connection closure to return.
            if (_acceptTask != null)
            {
                await _acceptTask.ConfigureAwait(false);
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2007:Consider calling ConfigureAwait on the awaited task",
            Justification = "Ensure continuations execute on the server scheduler if it is set")]
        private async ValueTask AcceptAsync()
        {
            while (true)
            {
                Connection connection;
                try
                {
                    connection = await _acceptor.AcceptAsync();
                    // TODO: Hack, remove once we get rid of the communicator
                    connection.Communicator = _server.Communicator;
                }
                catch (Exception ex)
                {
                    if (_server.Logger.IsEnabled(LogLevel.Error))
                    {
                        _server.Logger.LogAcceptingConnectionFailed(Endpoint.Transport, _acceptor, ex);
                    }

                    if (_shutdown)
                    {
                        return;
                    }

                    // We wait for one second to avoid running in a tight loop in case the failures occurs immediately
                    // again. Failures here are unexpected and could be considered fatal.
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    continue;
                }

                lock (_mutex)
                {
                    if (_shutdown)
                    {
                        connection.AbortAsync();
                        return;
                    }

                    _connections.Add(connection);

                    // We don't wait for the connection to be activated. This could take a while for some transports
                    // such as TLS based transports where the handshake requires few round trips between the client
                    // and server. Waiting could also cause a security issue if the client doesn't respond to the
                    // connection initialization as we wouldn't be able to accept new connections in the meantime.
                    _ = AcceptConnectionAsync(connection);
                }

                // Set the callback used to remove the connection from the factory.
                connection.Remove = connection => Remove(connection);
            }

            async Task AcceptConnectionAsync(Connection connection)
            {
                using var source = new CancellationTokenSource(_server.ConnectionOptions.AcceptTimeout);
                CancellationToken cancel = source.Token;
                try
                {
                    // Perform socket level initialization (handshake, etc)
                    await connection.AcceptAsync(
                        _server.ConnectionOptions.AuthenticationOptions,
                        cancel).ConfigureAwait(false);

                    // Check if the established connection can be trusted according to the server non-secure
                    // setting.
                    if (connection.CanTrust(_server.ConnectionOptions.AcceptNonSecure))
                    {
                        // Perform protocol level initialization
                        await connection.InitializeAsync(cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        if (_server.Logger.IsEnabled(LogLevel.Debug))
                        {
                            _server.Logger.LogConnectionNotTrusted(
                                connection.Endpoint.Transport);
                        }
                        // Connection not trusted, abort it.
                        await connection.AbortAsync().ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Failed incoming connection, abort the connection.
                    await connection.AbortAsync().ConfigureAwait(false);
                }
            }
        }
    }

    // IncomingConnectionFactory for datagram based transports
    internal sealed class DatagramIncomingConnectionFactory : IncomingConnectionFactory
    {
        internal override Endpoint Endpoint { get; }

        private readonly Connection _connection;
        private readonly Server _server;

        public override string ToString() => _connection.ToString()!;

        internal DatagramIncomingConnectionFactory(Server server, Endpoint endpoint)
        {
            _server = server;
            _connection = endpoint.CreateDatagramServerConnection(server);
            // TODO: Hack, remove once we get rid of the communicator
            _connection.Communicator = _server.Communicator;
            Endpoint = _connection.Endpoint;
        }

        internal override void Activate()
        {
            _ = _connection.AcceptAsync(null, default);
            _ = _connection.InitializeAsync(default);
        }

        internal override Task ShutdownAsync() =>
            _connection.GoAwayAsync(new ObjectDisposedException($"{typeof(Server).FullName}:{_server!.Name}"));
    }
}
