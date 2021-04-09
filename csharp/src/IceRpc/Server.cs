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
    public enum ColocationScope
    {
        Process,
        Communicator,
        None
    }

    /// <summary>TODO.</summary>
    public sealed class Server : IDispatcher, IAsyncDisposable
    {
        // temporary
        public ColocationScope ColocationScope { get; set; } = ColocationScope.Communicator;

        // temporary
        public Communicator? Communicator { get; set; }

        public IncomingConnectionOptions ConnectionOptions { get; set; } = new();

        public IDispatcher? Dispatcher { get; set; }

        /// <summary>Gets or sets the endpoint of this server.</summary>
        /// <value>The endpoint of this server. It cannot use a DNS name. If it's an IP endpoint with port 0
        /// <see cref="ListenAndServeAsync"/> replaces port 0 by the actual port selected by the operating system.
        /// </value>
        public string Endpoint
        {
            get => _endpoint?.ToString() ?? "";
            set
            {
                _endpoint = value.Length > 0 ? IceRpc.Endpoint.Parse(value) : null;
                _proxyEndpoint = _endpoint?.GetPublishedEndpoint(ProxyHost);
                Protocol = _endpoint?.Protocol ?? Protocol.Ice2;
            }
        }

        public ILoggerFactory? LoggerFactory
        {
            get => _loggerFactory;
            set
            {
                _loggerFactory = value;
                _logger = null; // clears existing logger, if there is one
            }
        }

        /// <summary>Gets of sets the Ice protocol used by this server. Setting <see cref="Endpoint"/> sets this value
        /// as well.</summary>
        public Protocol Protocol { get; set; } = Protocol.Ice2;

        public string ProxyEndpoint => _proxyEndpoint?.ToString() ?? "";

        public string ProxyHost { get; set; } = "localhost"; // System.Net.Dns.GetHostName();

        /// <summary>The local options of proxies received in requests or created using this server.</summary>
        public ProxyOptions ProxyOptions { get; set; } = new();

        /// <summary>Returns a task that completes when the server's shutdown is complete: see
        /// <see cref="ShutdownAsync"/>. This property can be retrieved before shutdown is initiated. A typical use-case
        /// is to call <c>await server.ShutdownComplete;</c> in the Main method of a server to prevent the server
        /// from exiting immediately.</summary>
        public Task ShutdownComplete => _shutdownCompleteSource.Task;

        /// <summary>Gets or sets the TaskScheduler used to dispatch requests.</summary>
        public TaskScheduler? TaskScheduler { get; set; }

        internal ILogger Logger => _logger ??= (_loggerFactory ?? Runtime.DefaultLoggerFactory).CreateLogger("IceRpc");

        private readonly Dictionary<(string Category, string Facet), IService> _categoryServiceMap = new();
        private AcceptorIncomingConnectionFactory? _colocatedConnectionFactory;

        private readonly Dictionary<string, IService> _defaultServiceMap = new();

        private Endpoint? _endpoint;

        private ILogger? _logger;
        private ILoggerFactory? _loggerFactory;

        private Endpoint? _proxyEndpoint;

        private IncomingConnectionFactory? _incomingConnectionFactory;

        // protects _serviceMap
        private readonly object _mutex = new();

        private readonly Dictionary<(string Path, string Facet), IService> _serviceMap = new();

        private bool _serving;

        private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Lazy<Task>? _shutdownTask;

        public Server()
        {
            // Temporary: creates a dispatcher that wraps the ASM held by this server.
            Dispatcher = new InlineDispatcher(
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
        }

        public T CreateRelativeProxy<T>(string path) where T : class, IServicePrx
        {
            // temporary
            ProxyOptions.Communicator ??= Communicator;

            return Proxy.GetFactory<T>().Create(path,
                                                Protocol,
                                                Protocol.GetEncoding(),
                                                ImmutableList<Endpoint>.Empty,
                                                GetColocatedConnection(),
                                                ProxyOptions);
        }

        public T CreateProxy<T>(string path) where T : class, IServicePrx
        {
            if (_proxyEndpoint == null)
            {
                throw new InvalidOperationException("cannot create a proxy using a server with no endpoint");
            }

            ProxyOptions options = ProxyOptions;
            options.Communicator ??= Communicator;

            if (_proxyEndpoint.IsDatagram && !options.IsOneway)
            {
                options = options.Clone();
                options.IsOneway = true;
            }

            return Proxy.GetFactory<T>().Create(path,
                                                Protocol,
                                                Protocol.GetEncoding(),
                                                ImmutableList.Create(_proxyEndpoint),
                                                connection: null, // TODO: give it a coloc connection except for UDP?
                                                options);
        }

        /// <summary>Runs the dispatcher in a try/catch block</summary>
        async ValueTask<OutgoingResponseFrame> IDispatcher.DispatchAsync(Current current, CancellationToken cancel)
        {
            // TODO: throw InvalidOperationException when _serving is false, which can occur with coloc invocations.

            // temporary
            ProxyOptions.Communicator ??= Communicator;

            if (Dispatcher is IDispatcher dispatcher)
            {
                try
                {
                    return await dispatcher.DispatchAsync(current, cancel).ConfigureAwait(false);
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
            else
            {
                throw new ServiceNotFoundException(RetryPolicy.OtherReplica);
            }
        }

        // Not async because we want to throw exceptions synchronously
        public Task ListenAndServeAsync(CancellationToken cancel = default)
        {
            if (_serving)
            {
                throw new InvalidOperationException(
                    $"'{nameof(ListenAndServeAsync)}' was already called on server '{this}'");
            }
            _serving = true;

            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
                }

                if (_endpoint is Endpoint endpoint)
                {
                    _incomingConnectionFactory = endpoint.IsDatagram ?
                        new DatagramIncomingConnectionFactory(this, endpoint) :
                        new AcceptorIncomingConnectionFactory(this, endpoint);

                    _endpoint = _incomingConnectionFactory.Endpoint;
                    _proxyEndpoint = _endpoint.GetPublishedEndpoint(ProxyHost);

                    _incomingConnectionFactory.Activate();
                }

                if (ColocationScope != ColocationScope.None)
                {
                    LocalServerRegistry.RegisterServer(this);
                }
            }

            if (Communicator?.GetPropertyAsBool("Ice.PrintAdapterReady") ?? false)
            {
                Console.Out.WriteLine($"{this} ready");
            }

            return WaitForShutdownAsync(cancel);

            async Task WaitForShutdownAsync(CancellationToken cancel)
            {
                try
                {
                    await ShutdownComplete.WaitAsync(cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Request "quick" shutdown that completes as soon as possible by cancelling everything it can.
                    await ShutdownAsync(cancel).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Shuts down this server. Once shut down, a server is disposed and can no longer be
        /// used. This method can be safely called multiple times and always returns the same task.</summary>
        public Task ShutdownAsync(CancellationToken _ = default)
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

                    // Shuts down the incoming connection factory to stop accepting new incoming requests or
                    // connections. This ensures that once ShutdownAsync returns, no new requests will be dispatched.
                    // Once _shutdownTask is non null, _incomingConnectionfactory cannot change, so no need to lock
                    // _mutex.
                    Task? colocShutdownTask = _colocatedConnectionFactory?.ShutdownAsync();
                    Task? incomingShutdownTask = _incomingConnectionFactory?.ShutdownAsync();

                    if (colocShutdownTask != null && incomingShutdownTask != null)
                    {
                        await Task.WhenAll(colocShutdownTask, incomingShutdownTask).ConfigureAwait(false);
                    }
                    else if (colocShutdownTask != null || incomingShutdownTask != null)
                    {
                        await (colocShutdownTask ?? incomingShutdownTask)!.ConfigureAwait(false);
                    }
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

        public override string ToString() => _endpoint?.ToString() ?? "coloc";

        // Below is the old ASM to be removed.

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
            UriParser.CheckPath(path, nameof(path));
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
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
            UriParser.CheckPath(path, nameof(path));
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
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
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
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
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
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
            UriParser.CheckPath(path, nameof(path));
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
            UriParser.CheckPath(path, nameof(path));
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

        internal Endpoint GetColocatedEndpoint()
        {
            // Lazy initialized because it needs a fully configured server, in particular Protocol.
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(Server).FullName}:{this}");
                }

                if (_colocatedConnectionFactory == null)
                {
                    _colocatedConnectionFactory
                        = new AcceptorIncomingConnectionFactory(this, new ColocatedEndpoint(this));
                    _colocatedConnectionFactory.Activate();
                }
            }
            return _colocatedConnectionFactory.Endpoint;
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
                    isLocal = _shutdownTask == null &&
                        proxy.Endpoints.Any(endpoint => endpoint == _endpoint || endpoint == _proxyEndpoint);
                }
            }

            return isLocal ? GetColocatedEndpoint() : null;
        }

        private Connection GetColocatedConnection()
        {
            // TODO: very temporary code
            var vt = Communicator!.ConnectAsync(GetColocatedEndpoint(), new(), default);
            return vt.IsCompleted ? vt.Result : vt.AsTask().Result;
        }
    }
}
