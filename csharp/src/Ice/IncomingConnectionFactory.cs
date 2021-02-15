// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ZeroC.Ice
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
        private readonly ObjectAdapter _adapter;
        private readonly Communicator _communicator;
        private readonly HashSet<Connection> _connections = new();
        private readonly object _mutex = new();
        private readonly ILogger _logger;
        private bool _shutdown;

        public override string ToString() => _acceptor.ToString()!;

        internal AcceptorIncomingConnectionFactory(ObjectAdapter adapter, Endpoint endpoint)
        {
            _communicator = adapter.Communicator;
            _logger = _communicator.Logger;
            _adapter = adapter;
            _acceptor = endpoint.Acceptor(_adapter);
            Endpoint = _acceptor.Endpoint;
        }

        internal override void Activate()
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogStartAcceptingConnections(Endpoint.TransportName, _acceptor);
            }

            // Start the asynchronous operation from the thread pool to prevent eventually accepting
            // synchronously new connections from this thread.
            lock (_mutex)
            {
                Debug.Assert(!_shutdown);
                _acceptTask = Task.Factory.StartNew(AcceptAsync,
                                                    default,
                                                    TaskCreationOptions.None,
                                                    _adapter.TaskScheduler ?? TaskScheduler.Default);
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
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogStopAcceptingConnections(Endpoint.TransportName, _acceptor);
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
            var exception = new ObjectDisposedException($"{typeof(ObjectAdapter).FullName}:{_adapter.Name}");
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
            Justification = "Ensure continuations execute on the object adapter scheduler if it is set")]
        private async ValueTask AcceptAsync()
        {
            while (true)
            {
                using (_acceptor.StartScope())
                {
                    Connection? connection = null;
                    try
                    {
                        connection = await _acceptor.AcceptAsync();
                        using (connection.StartScope())
                        {
                            if (_logger.IsEnabled(LogLevel.Debug))
                            {
                                _logger.LogAcceptingConnection(Endpoint.Transport);
                            }

                            lock (_mutex)
                            {
                                if (_shutdown)
                                {
                                    throw new ObjectDisposedException($"{typeof(ObjectAdapter).FullName}:{_adapter.Name}");
                                }

                                _connections.Add(connection);

                                // We don't wait for the connection to be activated. This could take a while for some transports
                                // such as TLS based transports where the handshake requires few round trips between the client
                                // and server.
                                _ = connection.InitializeAsync(default);
                            }
                            // Set the callback used to remove the connection from the factory.
                            connection.Remove = connection => Remove(connection);
                        }
                    }
                    catch (Exception exception)
                    {
                        using (connection?.StartScope())
                        {
                            if (connection != null)
                            {
                                await connection.GoAwayAsync(exception);
                            }
                            if (_shutdown)
                            {
                                return;
                            }

                            // We print an error and wait for one second to avoid running in a tight loop in case the
                            // failures occurs immediately again. Failures here are unexpected and could be considered
                            // fatal.
                            if (_logger.IsEnabled(LogLevel.Error))
                            {
                                _logger.LogAcceptingConnectionFailed(Endpoint.Transport, exception);
                            }
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            continue;
                        }
                    }
                }
            }
        }
    }

    // IncomingConnectionFactory for datagram based transports
    internal sealed class DatagramIncomingConnectionFactory : IncomingConnectionFactory
    {
        internal override Endpoint Endpoint { get; }

        private readonly Connection _connection;

        public override string ToString() => _connection.ToString()!;

        internal DatagramIncomingConnectionFactory(ObjectAdapter adapter, Endpoint endpoint)
        {
            _connection = endpoint.CreateDatagramServerConnection(adapter);
            using (_connection.StartScope())
            {
                Endpoint = _connection.Endpoint;
                _ = _connection.InitializeAsync(default);
            }
        }

        internal override void Activate()
        {
        }

        internal override Task ShutdownAsync()
        {
            var exception =
                new ObjectDisposedException($"{typeof(ObjectAdapter).FullName}:{_connection.Adapter!.Name}");
            return _connection.GoAwayAsync(exception);
        }
    }
}
