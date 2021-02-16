// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    /// <summary>The object adapter provides an up-call interface from the Ice run time to the implementation of Ice
    /// objects. The object adapter is responsible for receiving requests from endpoints, and for mapping between
    /// servants, identities, and proxies.</summary>
    public sealed class ObjectAdapter : IAsyncDisposable
    {
        /// <summary>Indicates under what circumstances this object adapter accepts non-secure incoming connections.
        /// </summary>
        public NonSecure AcceptNonSecure { get; }

        /// <summary>Returns the adapter ID of this object adapter, or the empty string if this object adapter does not
        /// have an adapter ID.</summary>
        public string AdapterId { get; }

        public ColocationScope ColocationScope { get; }

        /// <summary>Returns the communicator of this object adapter. It is used when unmarshaling proxies.</summary>
        /// <value>The communicator.</value>
        public Communicator Communicator { get; }

        /// <summary>The dispatch interceptors of this object adapter.</summary>
        public ImmutableList<DispatchInterceptor> DispatchInterceptors
        {
            get => _dispatchInterceptors;
            set => _dispatchInterceptors = value;
        }

        /// <summary>Returns the endpoints this object adapter is listening on.</summary>
        /// <returns>The endpoints configured on the object adapter; for IP endpoints, port 0 is substituted by the
        /// actual port selected by the operating system.</returns>
        public IReadOnlyList<Endpoint> Endpoints { get; } = ImmutableArray<Endpoint>.Empty;

        /// <summary>The locator registry proxy associated with this object adapter, if any. An indirect object adapter
        /// registers itself with the locator registry associated during activation, and unregisters during shutdown.
        /// </summary>
        /// <value>The locator registry proxy.</value>
        public ILocatorRegistryPrx? LocatorRegistry { get; }

        /// <summary>Returns the name of this object adapter. This name is used for logging.</summary>
        /// <value>The object adapter's name.</value>
        public string Name { get; }

        /// <summary>Gets the protocol of this object adapter. The format of this object adapter's Endpoints property
        /// determines this protocol.</summary>
        public Protocol Protocol { get; }

        /// <summary>Returns the endpoints listed in a direct proxy created by this object adapter.</summary>
        public IReadOnlyList<Endpoint> PublishedEndpoints { get; private set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>Returns the replica group ID of this object adapter, or the empty string if this object adapter
        /// does not belong to a replica group.</summary>
        public string ReplicaGroupId { get; }

        /// <summary>Indicates whether or not this object adapter serializes the dispatching of requests received
        /// over the same connection.</summary>
        /// <value>The serialize dispatch value.</value>
        public bool SerializeDispatch { get; }

        /// <summary>Returns a task that completes when the object adapter's shutdown is complete: see
        /// <see cref="ShutdownAsync"/>. This property can be retrieved before shutdown is initiated. A typical use-case
        /// is to call <c>await server.ShutdownComplete;</c> in the Main method of a server to prevent the server
        /// from exiting immediately.</summary>
        public Task ShutdownComplete => _shutdownCompleteSource.Task;

        /// <summary>Returns the TaskScheduler used to dispatch requests.</summary>
        public TaskScheduler? TaskScheduler { get; }

        internal int IncomingFrameMaxSize { get; }

        private static ulong _counter; // used to generate names for nameless object adapters.

        private Task? _activateTask;

        private readonly Dictionary<(string Category, string Facet), IObject> _categoryServantMap = new();
        private AcceptorIncomingConnectionFactory? _colocatedConnectionFactory;

        private readonly bool _datagramOnly;

        private readonly Dictionary<string, IObject> _defaultServantMap = new();
        private volatile ImmutableList<DispatchInterceptor> _dispatchInterceptors =
            ImmutableList<DispatchInterceptor>.Empty;

        private readonly Dictionary<(Identity Identity, string Facet), IObject> _identityServantMap = new();

        private readonly List<IncomingConnectionFactory> _incomingConnectionFactories = new();

        private readonly object _mutex = new();
        private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Lazy<Task>? _shutdownTask;

        /// <summary>Constructs an object adapter.</summary>
        public ObjectAdapter(Communicator communicator, ObjectAdapterOptions? options = null)
        {
            if (options == null)
            {
                options = new ObjectAdapterOptions();
            }

            Communicator = communicator;

            AcceptNonSecure = options.AcceptNonSecure;
            AdapterId = options.AdapterId;
            ColocationScope = options.ColocationScope;
            LocatorRegistry = options.LocatorRegistry;
            Name = options.Name.Length > 0 ? options.Name : $"server-{Interlocked.Increment(ref _counter)}";
            ReplicaGroupId = options.ReplicaGroupId;
            SerializeDispatch = options.SerializeDispatch;
            TaskScheduler = options.TaskScheduler;

            int frameMaxSize = options.IncomingFrameMaxSize ?? Communicator.IncomingFrameMaxSize;
            IncomingFrameMaxSize = frameMaxSize == 0 ? int.MaxValue : frameMaxSize;
            if (IncomingFrameMaxSize < 1024)
            {
                throw new ArgumentException("options.IncomingFrameMaxSize cannot be less than 1KB", nameof(options));
            }

            if (options.Endpoints.Length > 0)
            {
                if (UriParser.IsEndpointUri(options.Endpoints))
                {
                    Protocol = Protocol.Ice2;
                    Endpoints = UriParser.ParseEndpoints(options.Endpoints, Communicator);
                }
                else
                {
                    Protocol = Protocol.Ice1;
                    Endpoints = Ice1Parser.ParseEndpoints(options.Endpoints, communicator);

                    if (Endpoints.Count > 0 && Endpoints.All(e => e.IsDatagram))
                    {
                        _datagramOnly = true;
                        ColocationScope = ColocationScope.None;
                    }

                    // When the adapter is configured to only accept secure connections ensure that all
                    // configured endpoints only accept secure connections.
                    if (AcceptNonSecure == NonSecure.Never &&
                        Endpoints.FirstOrDefault(endpoint => !endpoint.IsAlwaysSecure) is Endpoint endpoint)
                    {
                        throw new ArgumentException(
                            $@"object adapter `{Name
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
                            @$"object adapter `{Name
                            }': only one endpoint is allowed when a dynamic IP port (:0) is configured",
                            nameof(options));
                    }

                    if (Endpoints[0].HasDnsHost)
                    {
                        throw new ArgumentException(
                            @$"object adapter `{Name
                            }': you can only use an IP address to configure an endpoint with a dynamic port (:0)",
                            nameof(options));
                    }
                }

                if (!Endpoints.Any(endpoint => endpoint.HasDnsHost))
                {
                    // Create the incoming factories immediately. This is needed to resolve dynamic ports.
                    _incomingConnectionFactories.AddRange(Endpoints.Select<Endpoint, IncomingConnectionFactory>(
                        endpoint =>
                        endpoint.IsDatagram ?
                            new DatagramIncomingConnectionFactory(this, endpoint) :
                            new AcceptorIncomingConnectionFactory(this, endpoint)));

                    // Replace Endpoints using the factories.
                    Endpoints = _incomingConnectionFactories.Select(factory => factory.Endpoint).ToImmutableArray();
                }
                // else keep Endpoints as-is. They do not contain port 0 since DNS name with port 0 is disallowed.
            }
            else
            {
                Protocol = options.Protocol;
            }

            if (options.PublishedEndpoints.Length > 0)
            {
                PublishedEndpoints = UriParser.IsEndpointUri(options.PublishedEndpoints) ?
                    UriParser.ParseEndpoints(options.PublishedEndpoints, Communicator) :
                    Ice1Parser.ParseEndpoints(options.PublishedEndpoints, Communicator, oaEndpoints: false);
            }

            if (PublishedEndpoints.Count == 0)
            {
                // If the PublishedEndpoints config property isn't set, we compute the published endpoints from
                // the endpoints.

                if (options.ServerName.Length == 0)
                {
                    throw new ArgumentException(
                        "both options.ServerName and options.PublishedEndpoints are empty",
                        nameof(options));
                }

                PublishedEndpoints = Endpoints.Select(endpoint => endpoint.GetPublishedEndpoint(options.ServerName)).
                    Distinct().ToImmutableArray();
            }

            if (ColocationScope != ColocationScope.None)
            {
                ObjectAdapterRegistry.RegisterObjectAdapter(this);
            }

            if (Communicator.TraceLevels.Transport >= 1 && PublishedEndpoints.Count > 0)
            {
                var sb = new StringBuilder("published endpoints for object adapter `");
                sb.Append(Name);
                sb.Append("':\n");
                sb.AppendEndpointList(PublishedEndpoints);
                Communicator.Logger.Trace(TraceLevels.TransportCategory, sb.ToString());
            }
        }

        /// <summary>Activates this object adapter. After activation, the object adapter can dispatch requests received
        /// through its endpoints. Also registers this object adapter with the locator (if set).</summary>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A task that completes when the activation completes.</returns>
        public async Task ActivateAsync(CancellationToken cancel = default)
        {
            List<Endpoint>? expandedEndpoints = null;
            if (Endpoints.Any(endpoint => endpoint.HasDnsHost))
            {
                expandedEndpoints = new();
                foreach (Endpoint endpoint in Endpoints)
                {
                    if (endpoint.HasDnsHost)
                    {
                        expandedEndpoints.AddRange(await endpoint.ExpandHostAsync(cancel).ConfigureAwait(false));
                    }
                    else
                    {
                        expandedEndpoints.Add(endpoint);
                    }
                }
            }

            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(ObjectAdapter).FullName}:{Name}");
                }

                // Activating twice the object adapter is incorrect
                if (_activateTask != null)
                {
                    throw new InvalidOperationException($"object adapter {Name} already activated");
                }

                if (expandedEndpoints != null)
                {
                    Debug.Assert(_incomingConnectionFactories.Count == 0);

                    _incomingConnectionFactories.AddRange(
                        expandedEndpoints.Select<Endpoint, IncomingConnectionFactory>(
                            endpoint =>
                            endpoint.IsDatagram ?
                                new DatagramIncomingConnectionFactory(this, endpoint) :
                                new AcceptorIncomingConnectionFactory(this, endpoint)));
                }

                // Activate the incoming connection factories to start accepting connections
                foreach (IncomingConnectionFactory factory in _incomingConnectionFactories)
                {
                    factory.Activate();
                }

                _activateTask = PerformActivateAsync(cancel);
            }

            await _activateTask.ConfigureAwait(false);

            if ((Communicator.GetPropertyAsBool("Ice.PrintAdapterReady") ?? false) && Name.Length > 0)
            {
                Console.Out.WriteLine($"{Name} ready");
            }

            async Task PerformActivateAsync(CancellationToken cancel)
            {
                // Register the published endpoints with the locator registry.

                if (PublishedEndpoints.Count == 0 || AdapterId.Length == 0 || LocatorRegistry == null)
                {
                    return; // nothing to do
                }

                try
                {
                    if (Protocol == Protocol.Ice1)
                    {
                        var proxy = IObjectPrx.Factory(new(Communicator,
                                                           new Identity("dummy", ""),
                                                           PublishedEndpoints[0].Protocol,
                                                           endpoints: PublishedEndpoints));

                        if (ReplicaGroupId.Length > 0)
                        {
                            await LocatorRegistry.SetReplicatedAdapterDirectProxyAsync(
                                AdapterId,
                                ReplicaGroupId,
                                proxy,
                                cancel: cancel).ConfigureAwait(false);
                        }
                        else
                        {
                            await LocatorRegistry.SetAdapterDirectProxyAsync(AdapterId,
                                                                             proxy,
                                                                             cancel: cancel).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await LocatorRegistry.RegisterAdapterEndpointsAsync(
                            AdapterId,
                            ReplicaGroupId,
                            PublishedEndpoints.ToEndpointDataList(),
                            cancel: cancel).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    if (Communicator.TraceLevels.Locator >= 1)
                    {
                        var sb = new StringBuilder("failed to register the endpoints of object adapter `");
                        sb.Append(Name);
                        sb.Append("' with the locator registry:\n");
                        sb.Append(ex);
                        Communicator.Logger.Trace(TraceLevels.LocatorCategory, sb.ToString());
                    }
                    throw;
                }

                if (Communicator.TraceLevels.Locator >= 1)
                {
                    var sb = new StringBuilder("registered the endpoints of object adapter `");
                    sb.Append(Name);
                    sb.Append("' with the locator registry\nendpoints = ");
                    sb.AppendEndpointList(PublishedEndpoints);

                    Communicator.Logger.Trace(TraceLevels.LocatorCategory, sb.ToString());
                }
            }
        }

        /// <summary>Adds a servant to this object adapter's Active Servant Map (ASM), using as key the provided
        /// identity and facet. Adding a servant with an identity and facet that are already in the ASM throws
        /// ArgumentException.</summary>
        /// <param name="identity">The identity of the Ice object incarnated by this servant. identity.Name cannot
        /// be empty.</param>
        /// <param name="facet">The facet of the Ice object.</param>
        /// <param name="servant">The servant to add.</param>
        /// <param name="proxyFactory">The proxy factory used to manufacture the returned proxy. Pass INamePrx.Factory
        /// for this parameter. See <see cref="CreateProxy{T}(Identity, string, ProxyFactory{T})"/>. </param>
        /// <returns>A proxy associated with this object adapter, object identity and facet.</returns>
        public T Add<T>(
            Identity identity,
            string facet,
            IObject servant,
            ProxyFactory<T> proxyFactory) where T : class, IObjectPrx
        {
            Add(identity, facet, servant);
            return CreateProxy(identity, facet, proxyFactory);
        }

        /// <summary>Adds a servant to this object adapter's Active Servant Map (ASM), using as key the provided
        /// identity and facet. Adding a servant with an identity and facet that are already in the ASM throws
        /// ArgumentException.</summary>
        /// <param name="identity">The identity of the Ice object incarnated by this servant. identity.Name cannot
        /// be empty.</param>
        /// <param name="facet">The facet of the Ice object.</param>
        /// <param name="servant">The servant to add.</param>
        public void Add(Identity identity, string facet, IObject servant)
        {
            CheckIdentity(identity);
            lock (_mutex)
            {
                // We check for deactivation here because we don't want to keep this servant when the adapter is being
                // deactivated or destroyed. In other languages, notably C++, keeping such a servant could lead to
                // circular references and leaks.
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(ObjectAdapter).FullName}:{Name}");
                }
                _identityServantMap.Add((identity, facet), servant);
            }
        }

        /// <summary>Adds a servant to this object adapter's Active Servant Map (ASM), using as key the provided
        /// identity and facet. Adding a servant with an identity and facet that are already in the ASM throws
        /// ArgumentException.</summary>
        /// <param name="identityAndFacet">A relative URI string [category/]identity[#facet].</param>
        /// <param name="servant">The servant to add.</param>
        /// <param name="proxyFactory">The proxy factory used to manufacture the returned proxy. Pass INamePrx.Factory
        /// for this parameter. See <see cref="CreateProxy{T}(string, ProxyFactory{T})"/>.</param>
        /// <returns>A proxy associated with this object adapter, object identity and facet.</returns>
        public T Add<T>(string identityAndFacet, IObject servant, ProxyFactory<T> proxyFactory) where T : class, IObjectPrx
        {
            (Identity identity, string facet) = UriParser.ParseIdentityAndFacet(identityAndFacet);
            return Add(identity, facet, servant, proxyFactory);
        }

        /// <summary>Adds a servant to this object adapter's Active Servant Map (ASM), using as key the provided
        /// identity and facet. Adding a servant with an identity and facet that are already in the ASM throws
        /// ArgumentException.</summary>
        /// <param name="identityAndFacet">A relative URI string [category/]identity[#facet].</param>
        /// <param name="servant">The servant to add.</param>
        public void Add(string identityAndFacet, IObject servant)
        {
            (Identity identity, string facet) = UriParser.ParseIdentityAndFacet(identityAndFacet);
            Add(identity, facet, servant);
        }

        /// <summary>Adds a servant to this object adapter's Active Servant Map (ASM), using as key the provided
        /// identity and the default (empty) facet.</summary>
        /// <param name="identity">The identity of the Ice object incarnated by this servant. identity.Name cannot
        /// be empty.</param>
        /// <param name="servant">The servant to add.</param>
        /// <param name="proxyFactory">The proxy factory used to manufacture the returned proxy. Pass INamePrx.Factory
        /// for this parameter. See <see cref="CreateProxy{T}(Identity, ProxyFactory{T})"/>.</param>
        /// <returns>A proxy associated with this object adapter, object identity and the default facet.</returns>
        public T Add<T>(Identity identity, IObject servant, ProxyFactory<T> proxyFactory) where T : class, IObjectPrx =>
            Add(identity, "", servant, proxyFactory);

        /// <summary>Adds a servant to this object adapter's Active Servant Map (ASM), using as key the provided
        /// identity and the default (empty) facet.</summary>
        /// <param name="identity">The identity of the Ice object incarnated by this servant. identity.Name cannot
        /// be empty.</param>
        /// <param name="servant">The servant to add.</param>
        public void Add(Identity identity, IObject servant) => Add(identity, "", servant);

        /// <summary>Adds a default servant to this object adapter's Active Servant Map (ASM), using as key the provided
        /// facet.</summary>
        /// <param name="facet">The facet.</param>
        /// <param name="servant">The default servant to add.</param>
        public void AddDefault(string facet, IObject servant)
        {
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(ObjectAdapter).FullName}:{Name}");
                }
                _defaultServantMap.Add(facet, servant);
            }
        }

        /// <summary>Adds a default servant to this object adapter's Active Servant Map (ASM), using as key the default
        /// (empty) facet.</summary>
        /// <param name="servant">The default servant to add.</param>
        public void AddDefault(IObject servant) => AddDefault("", servant);

        /// <summary>Adds a category-specific default servant to this object adapter's Active Servant Map (ASM), using
        /// as key the provided category and facet.</summary>
        /// <param name="category">The object identity category.</param>
        /// <param name="facet">The facet.</param>
        /// <param name="servant">The default servant to add.</param>
        public void AddDefaultForCategory(string category, string facet, IObject servant)
        {
            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(ObjectAdapter).FullName}:{Name}");
                }
                _categoryServantMap.Add((category, facet), servant);
            }
        }

        /// <summary>Adds a category-specific default servant to this object adapter's Active Servant Map (ASM), using
        /// as key the provided category and the default (empty) facet.</summary>
        /// <param name="category">The object identity category.</param>
        /// <param name="servant">The default servant to add.</param>
        public void AddDefaultForCategory(string category, IObject servant) =>
            AddDefaultForCategory(category, "", servant);

        /// <summary>Adds a servant to this object adapter's Active Servant Map (ASM), using as key a unique identity
        /// and the provided facet. This method creates the unique identity with a UUID name and an empty category.
        /// </summary>
        /// <param name="facet">The facet of the Ice object.</param>
        /// <param name="servant">The servant to add.</param>
        /// <param name="proxyFactory">The proxy factory used to manufacture the returned proxy. Pass INamePrx.Factory
        /// for this parameter. See <see cref="CreateProxy{T}(Identity, string, ProxyFactory{T})"/>.
        /// </param>
        /// <returns>A proxy associated with this object adapter, object identity and facet.</returns>
        public T AddWithUUID<T>(string facet, IObject servant, ProxyFactory<T> proxyFactory)
            where T : class, IObjectPrx =>
            Add(new Identity(Guid.NewGuid().ToString(), ""), facet, servant, proxyFactory);

        /// <summary>Adds a servant to this object adapter's Active Servant Map (ASM), using as key a unique identity
        /// and the default (empty) facet. This method creates the unique identity with a UUID name and an empty
        /// category.</summary>
        /// <param name="servant">The servant to add.</param>
        /// <param name="proxyFactory">The proxy factory used to manufacture the returned proxy. Pass INamePrx.Factory
        /// for this parameter. See <see cref="CreateProxy{T}(Identity, ProxyFactory{T})"/>.</param>
        /// <returns>A proxy associated with this object adapter, object identity and the default facet.</returns>
        public T AddWithUUID<T>(IObject servant, ProxyFactory<T> proxyFactory) where T : class, IObjectPrx =>
            AddWithUUID("", servant, proxyFactory);

          /// <summary>Creates a proxy for the object with the given identity and facet. If this object adapter is
        /// configured with an adapter ID, creates an indirect proxy that refers to the adapter ID. If a replica group
        /// ID is also defined, creates an indirect proxy that refers to the replica group ID. Otherwise, if no adapter
        /// ID is defined, creates a direct proxy containing this object adapter's published endpoints.</summary>
        /// <param name="identity">The object's identity.</param>
        /// <param name="facet">The facet.</param>
        /// <param name="factory">The proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// desired proxy type.</param>
        /// <returns>A proxy for the object with the given identity and facet.</returns>
        public T CreateProxy<T>(Identity identity, string facet, ProxyFactory<T> factory) where T : class, IObjectPrx
        {
            CheckIdentity(identity);

            lock (_mutex)
            {
                if (_shutdownTask != null)
                {
                    throw new ObjectDisposedException($"{typeof(ObjectAdapter).FullName}:{Name}");
                }

                ImmutableArray<string> location = ReplicaGroupId.Length > 0 ? ImmutableArray.Create(ReplicaGroupId) :
                    AdapterId.Length > 0 ? ImmutableArray.Create(AdapterId) : ImmutableArray<string>.Empty;

                Protocol protocol = PublishedEndpoints.Count > 0 ? PublishedEndpoints[0].Protocol : Protocol;

                var options = new ObjectPrxOptions(
                    Communicator,
                    identity,
                    protocol,
                    endpoints: AdapterId.Length == 0 ? PublishedEndpoints : ImmutableArray<Endpoint>.Empty,
                    facet: facet,
                    location: location,
                    oneway: _datagramOnly);

                return factory(options);
            }
        }

        /// <summary>Creates a proxy for the object with the given identity. If this object adapter is configured with
        /// an adapter id, creates an indirect proxy that refers to the adapter id. If a replica group id is also
        /// defined, creates an indirect proxy that refers to the replica group id. Otherwise, if no adapter
        /// id is defined, creates a direct proxy containing this object adapter's published endpoints.</summary>
        /// <param name="identity">The object's identity.</param>
        /// <param name="factory">The proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// desired proxy type.</param>
        /// <returns>A proxy for the object with the given identity.</returns>
        public T CreateProxy<T>(Identity identity, ProxyFactory<T> factory) where T : class, IObjectPrx =>
            CreateProxy(identity, "", factory);

        /// <summary>Creates a proxy for the object with the given identity and facet. If this object adapter is
        /// configured with an adapter id, creates an indirect proxy that refers to the adapter id. If a replica group
        /// id is also defined, creates an indirect proxy that refers to the replica group id. Otherwise, if no adapter
        /// id is defined, creates a direct proxy containing this object adapter's published endpoints.</summary>
        /// <param name="identityAndFacet">A relative URI string [category/]identity[#facet].</param>
        /// <param name="factory">The proxy factory. Use INamePrx.Factory for this parameter, where INamePrx is the
        /// desired proxy type.</param>
        /// <returns>A proxy for the object with the given identity and facet.</returns>
        public T CreateProxy<T>(string identityAndFacet, ProxyFactory<T> factory) where T : class, IObjectPrx
        {
            (Identity identity, string facet) = UriParser.ParseIdentityAndFacet(identityAndFacet);
            return CreateProxy(identity, facet, factory);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => new(ShutdownAsync());

        /// <summary>Finds a servant in the Active Servant Map (ASM), taking into account the servants and default
        /// servants currently in the ASM.</summary>
        /// <param name="identity">The identity of the Ice object.</param>
        /// <param name="facet">The facet of the Ice object.</param>
        /// <returns>The corresponding servant in the ASM, or null if the servant was not found.</returns>
        public IObject? Find(Identity identity, string facet = "")
        {
            lock (_mutex)
            {
                if (!_identityServantMap.TryGetValue((identity, facet), out IObject? servant))
                {
                    if (!_categoryServantMap.TryGetValue((identity.Category, facet), out servant))
                    {
                        _defaultServantMap.TryGetValue(facet, out servant);
                    }
                }
                return servant;
            }
        }

        /// <summary>Finds a servant in the Active Servant Map (ASM), taking into account the servants and default
        /// servants currently in the ASM.</summary>
        /// <param name="identityAndFacet">A relative URI string [category/]identity[#facet].</param>
        /// <returns>The corresponding servant in the ASM, or null if the servant was not found.</returns>
        public IObject? Find(string identityAndFacet)
        {
            (Identity identity, string facet) = UriParser.ParseIdentityAndFacet(identityAndFacet);
            return Find(identity, facet);
        }

        /// <summary>Removes a servant previously added to the Active Servant Map (ASM) using Add.</summary>
        /// <param name="identity">The identity of the Ice object.</param>
        /// <param name="facet">The facet of the Ice object.</param>
        /// <returns>The servant that was just removed from the ASM, or null if the servant was not found.</returns>
        public IObject? Remove(Identity identity, string facet = "")
        {
            lock (_mutex)
            {
                if (_identityServantMap.TryGetValue((identity, facet), out IObject? servant))
                {
                    _identityServantMap.Remove((identity, facet));
                }
                return servant;
            }
        }

        /// <summary>Removes a servant previously added to the Active Servant Map (ASM) using Add.</summary>
        /// <param name="identityAndFacet">A relative URI string [category/]identity[#facet].</param>
        /// <returns>The servant that was just removed from the ASM, or null if the servant was not found.</returns>
        public IObject? Remove(string identityAndFacet)
        {
            (Identity identity, string facet) = UriParser.ParseIdentityAndFacet(identityAndFacet);
            return Remove(identity, facet);
        }

        /// <summary>Removes a default servant previously added to the Active Servant Map (ASM) using AddDefault.
        /// </summary>
        /// <param name="facet">The facet.</param>
        /// <returns>The servant that was just removed from the ASM, or null if the servant was not found.</returns>
        public IObject? RemoveDefault(string facet = "")
        {
            lock (_mutex)
            {
                if (_defaultServantMap.TryGetValue(facet, out IObject? servant))
                {
                    _defaultServantMap.Remove(facet);
                }
                return servant;
            }
        }

        /// <summary>Removes a category-specific default servant previously added to the Active Servant Map (ASM) using
        /// AddDefaultForCategory.</summary>
        /// <param name="category">The category associated with this default servant.</param>
        /// <param name="facet">The facet.</param>
        /// <returns>The servant that was just removed from the ASM, or null if the servant was not found.</returns>
        public IObject? RemoveDefaultForCategory(string category, string facet = "")
        {
            lock (_mutex)
            {
                if (_categoryServantMap.TryGetValue((category, facet), out IObject? servant))
                {
                    _categoryServantMap.Remove((category, facet));
                }
                return servant;
            }
        }

        /// <summary>Shuts down this object adapter. Once shut down, an object adapter is disposed and can no longer be
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
                        ObjectAdapterRegistry.UnregisterObjectAdapter(this);
                    }

                    // Synchronously shuts down the incoming connection factories to stop accepting new incoming
                    // requests or connections. This ensures that once ShutdownAsync returns, no new requests will be
                    // dispatched. Calling ToArray is important here to ensure that all the ShutdownAsync calls are
                    // executed before we eventually hit an await (we want to make that once ShutdownAsync returns a
                    // Task, all the connections started closing).
                    // Once _shutdownTask is non null, _incomingConnectionfactories cannot change, so no need to lock
                    // _mutex.
                    Task[] tasks = _incomingConnectionFactories.Select(factory => factory.ShutdownAsync()).ToArray();

                    // Wait for activation to complete. This is necessary avoid out of order locator updates.
                    // _activateTask is readonly once _shutdownTask is non null.
                    if (_activateTask != null)
                    {
                        try
                        {
                            await _activateTask.ConfigureAwait(false);
                        }
                        catch
                        {
                            // Ignore
                        }
                    }

                    try
                    {
                        await UnregisterEndpointsAsync(default).ConfigureAwait(false);
                    }
                    catch
                    {
                        // We can't throw exceptions in deactivate so we ignore failures to unregister endpoints
                    }

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

        internal async ValueTask<OutgoingResponseFrame> DispatchAsync(
            IncomingRequestFrame request,
            Current current,
            CancellationToken cancel)
        {
            try
            {
                Debug.Assert(current.Adapter == this);
                IObject? servant = Find(current.Identity, current.Facet);
                if (servant == null)
                {
                    throw new ObjectNotExistException(RetryPolicy.OtherReplica);
                }

                ValueTask<OutgoingResponseFrame> DispatchAsync(IReadOnlyList<DispatchInterceptor> interceptors, int i)
                {
                    if (i < interceptors.Count)
                    {
                        DispatchInterceptor interceptor = interceptors[i++];
                        return interceptor(request,
                                           current,
                                           (request, current, cancel) => DispatchAsync(interceptors, i),
                                           cancel);
                    }
                    else
                    {
                        return servant.DispatchAsync(request, current, cancel);
                    }
                }

                return await DispatchAsync(_dispatchInterceptors, 0).ConfigureAwait(false);
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
                        if (Communicator.WarnDispatch)
                        {
                            Warning(ex);
                        }
                    }

                    return new OutgoingResponseFrame(request, actualEx);
                }
                else
                {
                    if (Communicator.WarnDispatch)
                    {
                        Warning(ex);
                    }
                    return OutgoingResponseFrame.WithVoidReturnValue(current);
                }
            }

            void Warning(Exception ex)
            {
                var output = new StringBuilder();
                output.Append("dispatch exception:");
                output.Append("\nidentity: ").Append(current.Identity.ToString(Communicator.ToStringMode));
                output.Append("\nfacet: ").Append(StringUtil.EscapeString(current.Facet, Communicator.ToStringMode));
                output.Append("\noperation: ").Append(current.Operation);
                if ((current.Connection as IPConnection)?.RemoteEndpoint is System.Net.IPEndPoint remoteEndpoint)
                {
                    output.Append("\nremote address: ").Append(remoteEndpoint);
                }
                output.Append('\n');
                output.Append(ex.ToString());
                Communicator.Logger.Warning(output.ToString());
            }
        }

        internal Endpoint? GetColocatedEndpoint(ObjectPrx proxy)
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

            if (proxy.IsWellKnown)
            {
                isLocal = Find(proxy.Identity, proxy.Facet) != null;
            }
            else if (proxy.IsIndirect)
            {
                // proxy is local if the proxy's location matches this adapter ID or replica group ID.
                isLocal = proxy.Location.Count == 1 &&
                    (proxy.Location[0] == AdapterId || proxy.Location[0] == ReplicaGroupId);
            }
            else
            {
                lock (_mutex)
                {
                    // Proxies which have at least one endpoint in common with the endpoints used by this object
                    // adapter's incoming connection factories are considered local.
                    isLocal = _shutdownTask == null && proxy.Endpoints.Any(endpoint =>
                        PublishedEndpoints.Any(publishedEndpoint => endpoint.IsLocal(publishedEndpoint)) ||
                        _incomingConnectionFactories.Any(factory => factory.IsLocal(endpoint)));
                }
            }

            if (isLocal)
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
            return null;
        }

        private static void CheckIdentity(Identity identity)
        {
            if (identity.Name.Length == 0)
            {
                throw new ArgumentException("identity name cannot be empty", nameof(identity));
            }
        }

        private async Task UnregisterEndpointsAsync(CancellationToken cancel)
        {
            // At this point, _locator is read-only.

            if (AdapterId.Length == 0 || LocatorRegistry == null)
            {
                return; // nothing to do
            }

            try
            {
                if (Protocol == Protocol.Ice1)
                {
                    if (ReplicaGroupId.Length > 0)
                    {
                        await LocatorRegistry.SetReplicatedAdapterDirectProxyAsync(
                            AdapterId,
                            ReplicaGroupId,
                            proxy: null,
                            cancel: cancel).ConfigureAwait(false);
                    }
                    else
                    {
                        await LocatorRegistry.SetAdapterDirectProxyAsync(AdapterId,
                                                                          proxy: null,
                                                                          cancel: cancel).ConfigureAwait(false);
                    }
                }
                else
                {
                    await LocatorRegistry.UnregisterAdapterEndpointsAsync(
                            AdapterId,
                            ReplicaGroupId,
                            cancel: cancel).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected if colocated call and OA is deactivated or the communicator is disposed, ignore.
            }
            catch (Exception ex)
            {
                if (Communicator.TraceLevels.Locator >= 1)
                {
                    Communicator.Logger.Trace(
                        TraceLevels.LocatorCategory,
                        @$"failed to unregister the endpoints of object adapter `{
                            Name}' from the locator registry:\n{ex}");
                }
                throw;
            }

            if (Communicator.TraceLevels.Locator >= 1)
            {
                Communicator.Logger.Trace(
                    TraceLevels.LocatorCategory,
                    $"unregistered the endpoints of object adapter `{Name}' from the locator registry");
            }
        }
    }
}
