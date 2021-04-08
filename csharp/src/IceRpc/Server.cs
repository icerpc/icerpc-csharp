// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The server provides an up-call interface from the Ice run time to the implementation of Ice
    /// objects. The server is responsible for receiving requests from endpoints, and for mapping between
    /// services, identities, and proxies.</summary>
    public sealed class Server : IAsyncDisposable
    {
        public ColocationScope ColocationScope { get; }

        /// <summary>Returns the communicator of this server. It is used when unmarshaling proxies.</summary>
        /// <value>The communicator.</value>
        public Communicator Communicator { get; }

        /// <summary>Returns the endpoints this server is listening on.</summary>
        /// <value>The endpoints configured on the server; for IP endpoints, port 0 is substituted by the
        /// actual port selected by the operating system.</value>
        public IReadOnlyList<Endpoint> Endpoints { get; } = ImmutableList<Endpoint>.Empty;

        /// <summary>Returns the name of this server. This name is used for logging.</summary>
        /// <value>The server's name.</value>
        public string Name { get; }

        /// <summary>Gets the protocol of this server. The format of this server's Endpoints property
        /// determines this protocol.</summary>
        public Protocol Protocol { get; }

        /// <summary>Returns the endpoints listed in a direct proxy created by this server.</summary>
        public IReadOnlyList<Endpoint> PublishedEndpoints { get; private set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>Returns a task that completes when the server's shutdown is complete: see
        /// <see cref="ShutdownAsync"/>. This property can be retrieved before shutdown is initiated. A typical use-case
        /// is to call <c>await server.ShutdownComplete;</c> in the Main method of a server to prevent the server
        /// from exiting immediately.</summary>
        public Task ShutdownComplete => _shutdownCompleteSource.Task;

        /// <summary>Returns the TaskScheduler used to dispatch requests.</summary>
        public TaskScheduler? TaskScheduler { get; }

        internal IncomingConnectionOptions ConnectionOptions { get; }
        internal bool IsDatagramOnly { get; }
        internal ILogger Logger { get; }
        internal ProxyOptions ProxyOptions { get; }

        private static ulong _counter; // used to generate names for nameless servers.

        private readonly Dictionary<(string Category, string Facet), IService> _categoryServiceMap = new();
        private AcceptorIncomingConnectionFactory? _colocatedConnectionFactory;

        private readonly Dictionary<string, IService> _defaultServiceMap = new();

        private IDispatcher? _dispatcher;

        private readonly List<IncomingConnectionFactory> _incomingConnectionFactories = new();

        // protects _activated, _dispatchInterceptorList, _serviceMap,
        private readonly object _mutex = new();

        private readonly Dictionary<(string Path, string Facet), IService> _serviceMap = new();

        private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Lazy<Task>? _shutdownTask;

        /// <summary>Constructs a server.</summary>
        public Server(Communicator communicator)
            : this(communicator, new())
        {
        }

        /// <summary>Constructs a server.</summary>
        public Server(Communicator communicator, ServerOptions options)
        {
            Communicator = communicator;

            ColocationScope = options.ColocationScope;
            Name = options.Name.Length > 0 ? options.Name : $"server-{Interlocked.Increment(ref _counter)}";
            TaskScheduler = options.TaskScheduler;
            Logger = options.LoggerFactory.CreateLogger("IceRpc");

            ProxyOptions = options.ProxyOptions.Clone();
            ProxyOptions.Communicator = communicator;
            ConnectionOptions = options.ConnectionOptions.Clone();

            if (ConnectionOptions.AcceptNonSecure == NonSecure.Never && ConnectionOptions.AuthenticationOptions == null)
            {
                throw new ArgumentException(
                    "server is configured to only accept secure connections but authentication options are not set",
                    nameof(options));
            }

            if (options.Endpoints.Length > 0)
            {
                if (UriParser.IsEndpointUri(options.Endpoints))
                {
                    Protocol = Protocol.Ice2;
                    Endpoints = UriParser.ParseEndpoints(options.Endpoints);
                }
                else
                {
                    Protocol = Protocol.Ice1;
                    Endpoints = Ice1Parser.ParseEndpoints(options.Endpoints);

                    if (Endpoints.Count > 0 && Endpoints.All(e => e.IsDatagram))
                    {
                        IsDatagramOnly = true;
                        ColocationScope = ColocationScope.None;
                    }

                    // When the server is configured to only accept secure connections ensure that all
                    // configured endpoints only accept secure connections.
                    if (ConnectionOptions.AcceptNonSecure == NonSecure.Never &&
                        Endpoints.FirstOrDefault(endpoint => !endpoint.IsAlwaysSecure) is Endpoint endpoint)
                    {
                        throw new ArgumentException(
                            $@"server `{Name
                            }' is configured to only accept secure connections but endpoint `{endpoint
                            }' accepts non-secure connections",
                            nameof(options));
                    }
                }
                Debug.Assert(Endpoints.Count > 0);

                if (Endpoints.Any(endpoint => endpoint is IPEndpoint ipEndpoint && ipEndpoint.Port == 0))
                {
                    if (Endpoints.Count > 1)
                    {
                        throw new ArgumentException(
                            @$"server `{Name
                            }': only one endpoint is allowed when a dynamic IP port (:0) is configured",
                            nameof(options));
                    }
                }

                // Create the incoming factories immediately. This is needed to resolve dynamic ports.
                _incomingConnectionFactories.AddRange(Endpoints.Select<Endpoint, IncomingConnectionFactory>(
                    endpoint => endpoint.IsDatagram ?
                        new DatagramIncomingConnectionFactory(this, endpoint) :
                        new AcceptorIncomingConnectionFactory(this, endpoint)));

                // Replace Endpoints using the factories.
                Endpoints = _incomingConnectionFactories.Select(factory => factory.Endpoint).ToImmutableList();
            }
            else
            {
                Protocol = options.Protocol;
            }

            if (options.PublishedEndpoints.Length > 0)
            {
                PublishedEndpoints = UriParser.IsEndpointUri(options.PublishedEndpoints) ?
                    UriParser.ParseEndpoints(options.PublishedEndpoints) :
                    Ice1Parser.ParseEndpoints(options.PublishedEndpoints);
            }

            if (PublishedEndpoints.Count == 0)
            {
                // If the PublishedEndpoints config property isn't set, we compute the published endpoints from
                // the endpoints.

                if (options.PublishedHost.Length == 0)
                {
                    throw new ArgumentException(
                        "both options.PublishedHost and options.PublishedEndpoints are empty",
                        nameof(options));
                }

                PublishedEndpoints = Endpoints.Select(endpoint => endpoint.GetPublishedEndpoint(options.PublishedHost)).
                    Distinct().ToImmutableList();
            }

            if (ColocationScope != ColocationScope.None)
            {
                LocalServerRegistry.RegisterServer(this);
            }

            if (PublishedEndpoints.Count > 0)
            {
                Logger.LogServerPublishedEndpoints(Name, PublishedEndpoints);
            }
        }

        // Temporary: creates a dispatcher that wraps the ASM held by this server.
        public void Activate()
        {
            var dispatcher = new InlineDispatcher(
                (current, cancel) =>
                {
                    Debug.Assert(current.Server == this);
                    IService? service = Find(current.Path, current.IncomingRequestFrame.Facet);
                    if (service == null)
                    {
                        throw new ServiceNotFoundException(RetryPolicy.OtherReplica);
                    }
                    return service.DispatchAsync(current, cancel);
                 });

            Activate(dispatcher);
        }

        /// <summary>Activates this server. After activation, the server can dispatch requests received through its
        /// endpoints.</summary>
        public void Activate(IDispatcher dispatcher)
        {
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{Name}");
                }

                // Activating twice the server is incorrect
                if (_dispatcher != null)
                {
                    throw new InvalidOperationException($"server {Name} already activated");
                }
                _dispatcher = dispatcher;

                // Activate the incoming connection factories to start accepting connections
                foreach (IncomingConnectionFactory factory in _incomingConnectionFactories)
                {
                    factory.Activate();
                }
            }

            if ((Communicator.GetPropertyAsBool("Ice.PrintAdapterReady") ?? false) && Name.Length > 0)
            {
                Console.Out.WriteLine($"{Name} ready");
            }
        }

        /// <summary>Adds a service to this server's Active Service Map (ASM).</summary>
        /// <param name="path">The path of the service.</param>
        /// <param name="facet">The facet of the service.</param>
        /// <param name="service">The service to add.</param>
        /// <param name="proxyFactory">The proxy factory used to manufacture the returned proxy. Pass INamePrx.Factory
        /// for this parameter.</param>
        /// <returns>A proxy associated with this server, path and facet.</returns>
        public T Add<T>(
            string path,
            string facet,
            IService service,
            IProxyFactory<T> proxyFactory) where T : class, IServicePrx
        {
            UriParser.CheckPath(path);
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{Name}");
                }
                _serviceMap.Add((path, facet), service);
            }

            if (facet.Length > 0)
            {
                return proxyFactory.Create(this, path).WithFacet<T>(facet);
            }
            else
            {
                return proxyFactory.Create(this, path);
            }
        }

        public T Add<T>(
            string path,
            IService service,
            IProxyFactory<T> proxyFactory) where T : class, IServicePrx =>
            Add(path, "", service, proxyFactory);

        /// <summary>Adds a service to this server's Active Service Map (ASM), using as key the provided path and facet.
        /// </summary>
        /// <param name="path">The path to the service.</param>
        /// <param name="facet">The facet of the service.</param>
        /// <param name="service">The service to add.</param>
        public void Add(string path, string facet, IService service)
        {
            UriParser.CheckPath(path);
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{Name}");
                }
                _serviceMap.Add((path, facet), service);
            }
        }

        public void Add(string path, IService service) => Add(path, "", service);

        /// <summary>Adds a default service to this server's Active Service Map (ASM), using as key the provided
        /// facet.</summary>
        /// <param name="facet">The facet.</param>
        /// <param name="service">The default service to add.</param>
        public void AddDefault(string facet, IService service)
        {
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{Name}");
                }
                _defaultServiceMap.Add(facet, service);
            }
        }

        /// <summary>Adds a default service to this server's Active Service Map (ASM), using as key the default
        /// (empty) facet.</summary>
        /// <param name="service">The default service to add.</param>
        public void AddDefault(IService service) => AddDefault("", service);

        /// <summary>Adds a category-specific default service to this server's Active Service Map (ASM), using
        /// as key the provided category and facet.</summary>
        /// <param name="category">The object identity category.</param>
        /// <param name="facet">The facet.</param>
        /// <param name="service">The default service to add.</param>
        public void AddDefaultForCategory(string category, string facet, IService service)
        {
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{Name}");
                }
                _categoryServiceMap.Add((category, facet), service);
            }
        }

        /// <summary>Adds a category-specific default service to this server's Active Service Map (ASM), using
        /// as key the provided category and the default (empty) facet.</summary>
        /// <param name="category">The object identity category.</param>
        /// <param name="service">The default service to add.</param>
        public void AddDefaultForCategory(string category, IService service) =>
            AddDefaultForCategory(category, "", service);

        /// <summary>Adds a service to this server's Active Service Map (ASM), using as key a unique identity
        /// and the provided facet. This method creates the unique identity with a UUID name and an empty category.
        /// </summary>
        /// <param name="facet">The facet of the Ice object.</param>
        /// <param name="service">The service to add.</param>
        /// <param name="proxyFactory">The proxy factory used to manufacture the returned proxy. Pass INamePrx.Factory
        /// for this parameter.</param>
        /// <returns>A proxy associated with this server, object identity and facet.</returns>
        public T AddWithUUID<T>(string facet, IService service, IProxyFactory<T> proxyFactory)
            where T : class, IServicePrx =>
            Add($"/{Guid.NewGuid().ToString()}", facet, service, proxyFactory);

        /// <summary>Adds a service to this server's Active Service Map (ASM), using as key a unique identity
        /// and the default (empty) facet. This method creates the unique identity with a UUID name and an empty
        /// category.</summary>
        /// <param name="service">The service to add.</param>
        /// <param name="proxyFactory">The proxy factory used to manufacture the returned proxy. Pass INamePrx.Factory
        /// for this parameter.</param>
        /// <returns>A proxy associated with this server, object identity and the default facet.</returns>
        public T AddWithUUID<T>(IService service, IProxyFactory<T> proxyFactory) where T : class, IServicePrx =>
            AddWithUUID("", service, proxyFactory);

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => new(ShutdownAsync());

        /// <summary>Finds a service in the Active Service Map (ASM), taking into account the services and default
        /// services currently in the ASM.</summary>
        /// <param name="path">The path to the service.</param>
        /// <param name="facet">The facet of the service.</param>
        /// <returns>The corresponding service in the ASM, or null if the service was not found.</returns>
        public IService? Find(string path, string facet = "")
        {
            UriParser.CheckPath(path);
            lock (_mutex)
            {
                if (!_serviceMap.TryGetValue((path, facet), out IService? service))
                {
                    bool found = false;
                    try
                    {
                        found = _categoryServiceMap.TryGetValue((Identity.FromPath(path).Category, facet), out service);
                    }
                    catch (FormatException)
                    {
                        // bad path ignored, found remains false
                    }

                    if (!found)
                    {
                        _defaultServiceMap.TryGetValue(facet, out service);
                    }
                }
                return service;
            }
        }

        /// <summary>Removes a service previously added to the Active Service Map (ASM) using Add.</summary>
        /// <param name="path">The path to the service.</param>
        /// <param name="facet">The facet of the service.</param>
        /// <returns>The service that was just removed from the ASM, or null if the service was not found.</returns>
        public IService? Remove(string path, string facet = "")
        {
            UriParser.CheckPath(path);
            lock (_mutex)
            {
                if (_serviceMap.TryGetValue((path, facet), out IService? service))
                {
                    _serviceMap.Remove((path, facet));
                }
                return service;
            }
        }

        /// <summary>Removes a default service previously added to the Active Service Map (ASM) using AddDefault.
        /// </summary>
        /// <param name="facet">The facet.</param>
        /// <returns>The service that was just removed from the ASM, or null if the service was not found.</returns>
        public IService? RemoveDefault(string facet = "")
        {
            lock (_mutex)
            {
                if (_defaultServiceMap.TryGetValue(facet, out IService? service))
                {
                    _defaultServiceMap.Remove(facet);
                }
                return service;
            }
        }

        /// <summary>Removes a category-specific default service previously added to the Active Service Map (ASM) using
        /// AddDefaultForCategory.</summary>
        /// <param name="category">The category associated with this default service.</param>
        /// <param name="facet">The facet.</param>
        /// <returns>The service that was just removed from the ASM, or null if the service was not found.</returns>
        public IService? RemoveDefaultForCategory(string category, string facet = "")
        {
            lock (_mutex)
            {
                if (_categoryServiceMap.TryGetValue((category, facet), out IService? service))
                {
                    _categoryServiceMap.Remove((category, facet));
                }
                return service;
            }
        }

        /// <summary>Shuts down this server. Once shut down, a server is disposed and can no longer be
        /// used. This method can be safely called multiple times and always returns the same task.</summary>
        public Task ShutdownAsync()
        {
            // We create the lazy shutdown task with the mutex locked then we create the actual task immediately (and
            // synchronously) after releasing the lock.
            lock (_mutex)
            {
                _shutdownTask ??= new Lazy<Task>(() => PerformShutdownAsync());
            }
            return _shutdownTask.Value;

            async Task PerformShutdownAsync()
            {
                try
                {
                    if (ColocationScope != ColocationScope.None)
                    {
                        // no longer available for coloc connections.
                        LocalServerRegistry.UnregisterServer(this);
                    }

                    // Synchronously shuts down the incoming connection factories to stop accepting new incoming
                    // requests or connections. This ensures that once ShutdownAsync returns, no new requests will be
                    // dispatched. Calling ToArray is important here to ensure that all the ShutdownAsync calls are
                    // executed before we eventually hit an await (we want to make that once ShutdownAsync returns a
                    // Task, all the connections started closing).
                    // Once _shutdownTask is non null, _incomingConnectionfactories cannot change, so no need to lock
                    // _mutex.
                    Task[] tasks = _incomingConnectionFactories.Select(factory => factory.ShutdownAsync()).ToArray();

                    if (_colocatedConnectionFactory != null)
                    {
                        await _colocatedConnectionFactory.ShutdownAsync().ConfigureAwait(false);
                    }

                    // Wait for the incoming connection factories to be shut down.
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                finally
                {
                    // The continuation is executed asynchronously (see _shutdownCompleteSource's construction). This
                    // way, even if the continuation blocks waiting on ShutdownAsync to complete (with incorrect code
                    // using Result or Wait()), ShutdownAsync will complete.
                    _shutdownCompleteSource.TrySetResult(null);
                }
            }
        }

        /// <summary>Runs the dispatcher in a try/catch block</summary>
        internal async ValueTask<OutgoingResponseFrame> DispatchAsync(
            Current current,
            CancellationToken cancel)
        {
            // TODO: temporary work-around
            if (_dispatcher == null)
            {
                lock(_mutex)
                {
                    if (_dispatcher == null)
                    {
                        Activate();
                    }
                }
            }

            // Dispatch works only once the server is activated, which sets _dispatcher.
            Debug.Assert(_dispatcher != null);

            try
            {
                return await _dispatcher.DispatchAsync(current, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!current.IsOneway)
                {
                    RemoteException actualEx;
                    if (ex is RemoteException remoteEx && !remoteEx.ConvertToUnhandled)
                    {
                        actualEx = remoteEx;
                    }
                    else
                    {
                        actualEx = new UnhandledException(ex);
                        Logger.LogDispatchException(current.IncomingRequestFrame, ex);
                    }

                    return new OutgoingResponseFrame(current.IncomingRequestFrame, actualEx);
                }
                else
                {
                    Logger.LogDispatchException(current.IncomingRequestFrame, ex);
                    return OutgoingResponseFrame.WithVoidReturnValue(current);
                }
            }
        }

        internal Endpoint? GetColocatedEndpoint()
        {
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    return null;
                }

                if (_colocatedConnectionFactory == null)
                {
                    _colocatedConnectionFactory =
                        new AcceptorIncomingConnectionFactory(this, new ColocatedEndpoint(this));

                    // It's safe to start the connection within the synchronization, this isn't supposed to block
                    // for colocated connections.
                    _colocatedConnectionFactory.Activate();
                }
                return _colocatedConnectionFactory.Endpoint;
            }
        }

        internal Endpoint? GetColocatedEndpoint(ServicePrx proxy)
        {
            Debug.Assert(ColocationScope != ColocationScope.None);

            if (ColocationScope == ColocationScope.Communicator && Communicator != proxy.Communicator)
            {
                return null;
            }

            if (proxy.Protocol != Protocol)
            {
                return null;
            }

            bool isLocal = false;

            if (proxy.IsWellKnown || proxy.IsRelative)
            {
                isLocal = Find(proxy.Path, proxy.Facet) != null;
            }
            else
            {
                lock (_mutex)
                {
                    // Proxies which have at least one endpoint in common with the endpoints used by this object
                    // server's incoming connection factories are considered local.
                    isLocal = _shutdownTask == null && proxy.Endpoints.Any(endpoint =>
                        PublishedEndpoints.Any(publishedEndpoint => endpoint == publishedEndpoint) ||
                        _incomingConnectionFactories.Any(factory => endpoint == factory.Endpoint));
                }
            }

            return isLocal ? GetColocatedEndpoint() : null;
        }
    }
}
