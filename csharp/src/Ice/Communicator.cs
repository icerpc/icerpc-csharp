// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroC.Ice
{
    internal sealed class BufWarnSizeInfo
    {
        // Whether send size warning has been emitted
        public bool SndWarn;

        // The send size for which the warning was emitted
        public int SndSize;

        // Whether receive size warning has been emitted
        public bool RcvWarn;

        // The receive size for which the warning was emitted
        public int RcvSize;
    }

    /// <summary>The central object in Ice. One or more communicators can be instantiated for an Ice application.
    /// </summary>
    public sealed partial class Communicator : IAsyncDisposable
    {
        private class ObserverUpdater : Instrumentation.IObserverUpdater
        {
            public ObserverUpdater(Communicator communicator) => _communicator = communicator;

            public void UpdateConnectionObservers() => _communicator.UpdateConnectionObservers();

            private readonly Communicator _communicator;
        }

        /// <summary>Indicates under what conditions the object adapters created by this communicator accept non-secure
        /// incoming connections. This property corresponds to the Ice.AcceptNonSecure configuration property. It can
        /// be overridden for each object adapter by the object adapter property with the same name.</summary>
        // TODO: update doc with default value for AcceptNonSecure - it's currently Always but should be Never
        public NonSecure AcceptNonSecure { get; }

        /// <summary>The connection close timeout.</summary>
        public TimeSpan CloseTimeout { get; }
        /// <summary>The connection establishment timeout.</summary>
        public TimeSpan ConnectTimeout { get; }

        /// <summary>Each time you send a request without an explicit context parameter, Ice sends automatically the
        /// per-thread CurrentContext combined with the proxy's context.</summary>
        public SortedDictionary<string, string> CurrentContext
        {
            get
            {
                try
                {
                    if (_currentContext.IsValueCreated)
                    {
                        Debug.Assert(_currentContext.Value != null);
                        return _currentContext.Value;
                    }
                    else
                    {
                        _currentContext.Value = new SortedDictionary<string, string>();
                        return _currentContext.Value;
                    }
                }
                catch (ObjectDisposedException)
                {
                    return new SortedDictionary<string, string>();
                }
            }
            set
            {
                try
                {
                    _currentContext.Value = value;
                }
                catch (ObjectDisposedException ex)
                {
                    throw new CommunicatorDisposedException(ex);
                }
            }
        }

        /// <summary>The default context for proxies created using this communicator. Changing the value of
        /// DefaultContext does not change the context of previously created proxies.</summary>
        public IReadOnlyDictionary<string, string> DefaultContext
        {
            get => _defaultContext;
            set => _defaultContext = value.ToImmutableSortedDictionary();
        }

        /// <summary>The default dispatch interceptors for object adapters created using this communicator. Changing the
        /// value of DefaultDispatchInterceptors does not change the dispatch interceptors of previously created object
        /// adapters.</summary>
        public IReadOnlyList<DispatchInterceptor> DefaultDispatchInterceptors
        {
            get => _defaultDispatchInterceptors;
            set => _defaultDispatchInterceptors = value.ToImmutableList();
        }

        /// <summary>The default invocation interceptors for proxies created using this communicator. Changing the value
        /// of DefaultInvocationInterceptors does not change the invocation interceptors of previously created proxies.
        /// </summary>
        public IReadOnlyList<InvocationInterceptor> DefaultInvocationInterceptors
        {
            get => _defaultInvocationInterceptors;
            set => _defaultInvocationInterceptors = value.ToImmutableList();
        }

        /// <summary>The default locator for this communicator. To disable the default locator, null can be used.
        /// All newly created proxies and object adapters will use this default locator. Note that setting this property
        /// has no effect on existing proxies or object adapters.</summary>
        public ILocatorPrx? DefaultLocator
        {
            get => _defaultLocator;
            set => _defaultLocator = value;
        }

        /// <summary>Gets the communicator's preference for reusing existing connections.</summary>
        public bool DefaultPreferExistingConnection { get; }

        /// <summary>Gets the communicator's preference for establishing non-secure connections.</summary>
        public NonSecure DefaultPreferNonSecure { get; }

        /// <summary>Gets the default source address value used by proxies created with this communicator.</summary>
        public IPAddress? DefaultSourceAddress { get; }

        /// <summary>Gets the default invocation timeout value used by proxies created with this communicator.
        /// </summary>
        public TimeSpan DefaultInvocationTimeout { get; }

        /// <summary>Gets the default value for the locator cache timeout used by proxies created with this
        /// communicator.</summary>
        public TimeSpan DefaultLocatorCacheTimeout { get; }

        /// <summary>The logger for this communicator.</summary>
        public ILogger Logger
        {
            get => _logger;
            set => _logger = value;
        }

        /// <summary>Gets the communicator observer used by the Ice run-time or null if a communicator observer
        /// was not set during communicator construction.</summary>
        public Instrumentation.ICommunicatorObserver? Observer { get; }

        /// <summary>The output mode or format for ToString on Ice proxies when the protocol is ice1. See
        /// <see cref="Ice.ToStringMode"/>.</summary>
        public ToStringMode ToStringMode { get; }

        // The communicator's cancellation token is notified of cancellation when the communicator is destroyed.
        internal CancellationToken CancellationToken
        {
            get
            {
                try
                {
                    return _cancellationTokenSource.Token;
                }
                catch (ObjectDisposedException ex)
                {
                    throw new CommunicatorDisposedException(ex);
                }
            }
        }

        internal int ClassGraphMaxDepth { get; }
        internal CompressionLevel CompressionLevel { get; }
        internal int CompressionMinSize { get; }

        internal TimeSpan IdleTimeout { get; }
        internal int IncomingFrameMaxSize { get; }
        internal bool IsDisposed => _destroyTask != null;
        internal bool KeepAlive { get; }
        internal int MaxBidirectionalStreams { get; }
        internal int MaxUnidirectionalStreams { get; }
        internal int SlicPacketMaxSize { get; }

        /// <summary>Gets the maximum number of invocation attempts made to send a request including the original
        /// invocation. It must be a number greater than 0.</summary>
        internal int InvocationMaxAttempts { get; }
        internal int RetryBufferMaxSize { get; }
        internal int RetryRequestMaxSize { get; }
        internal string ServerName { get; }
        internal SslEngine SslEngine { get; }
        internal TraceLevels TraceLevels { get; private set; }
        internal bool WarnConnections { get; }
        internal bool WarnDatagrams { get; }
        internal bool WarnDispatch { get; }
        internal bool WarnUnknownProperties { get; }

        private static string[] _emptyArgs = Array.Empty<string>();

        private static readonly Dictionary<string, Assembly> _loadedAssemblies = new();

        private static bool _oneOffDone;

        private static bool _printProcessIdDone;

        private static readonly object _staticMutex = new object();
        private readonly HashSet<string> _adapterNamesInUse = new();
        private readonly List<ObjectAdapter> _adapters = new();
        private readonly bool _backgroundLocatorCacheUpdates;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ConcurrentDictionary<string, Func<AnyClass>?> _classFactoryCache = new();
        private readonly ConcurrentDictionary<int, Func<AnyClass>?> _compactIdCache = new();
        private readonly ThreadLocal<SortedDictionary<string, string>> _currentContext = new();
        private volatile ImmutableSortedDictionary<string, string> _defaultContext =
            ImmutableSortedDictionary<string, string>.Empty;
        private volatile ImmutableList<InvocationInterceptor> _defaultInvocationInterceptors =
            ImmutableList<InvocationInterceptor>.Empty;
        private volatile ILocatorPrx? _defaultLocator;
        private volatile ImmutableList<DispatchInterceptor> _defaultDispatchInterceptors =
            ImmutableList<DispatchInterceptor>.Empty;
        private Task? _destroyTask;

        private readonly IDictionary<Transport, Ice1EndpointFactory> _ice1TransportRegistry =
            new ConcurrentDictionary<Transport, Ice1EndpointFactory>();

        private readonly IDictionary<string, (Ice1EndpointParser, Transport)> _ice1TransportNameRegistry =
            new ConcurrentDictionary<string, (Ice1EndpointParser, Transport)>();

        private readonly IDictionary<Transport, (Ice2EndpointFactory, Ice2EndpointParser)> _ice2TransportRegistry =
            new ConcurrentDictionary<Transport, (Ice2EndpointFactory, Ice2EndpointParser)>();

        private readonly IDictionary<string, (Ice2EndpointParser, Transport)> _ice2TransportNameRegistry =
            new ConcurrentDictionary<string, (Ice2EndpointParser, Transport)>();

        private readonly ConcurrentDictionary<ILocatorPrx, LocatorInfo> _locatorInfoMap = new();

        private volatile ILogger _logger;
        private readonly object _mutex = new object();

        private readonly ConcurrentDictionary<string, Func<string?, RemoteExceptionOrigin?, RemoteException>?> _remoteExceptionFactoryCache =
            new();
        private int _retryBufferSize;

        private readonly Dictionary<Transport, BufWarnSizeInfo> _setBufWarnSize = new();
        private readonly TaskCompletionSource<object?> _shutdownCompleteSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Lazy<Task>? _shutdownTask;

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="logger">The logger used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the new communicator.</param>
        /// <param name="tlsClientOptions">Client side configuration for TLS connections.</param>
        /// <param name="tlsServerOptions">Server side configuration for TLS connections.</param>
        public Communicator(
            IReadOnlyDictionary<string, string> properties,
            ILogger? logger = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            TlsClientOptions? tlsClientOptions = null,
            TlsServerOptions? tlsServerOptions = null)
            : this(ref _emptyArgs,
                   appSettings: null,
                   logger,
                   observer,
                   properties,
                   tlsClientOptions,
                   tlsServerOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="args">An array of command-line arguments used to set or override Ice.* properties.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="logger">The logger used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the new communicator.</param>
        /// <param name="tlsClientOptions">Client side configuration for TLS connections.</param>
        /// <param name="tlsServerOptions">Server side configuration for TLS connections.</param>
        public Communicator(
            ref string[] args,
            IReadOnlyDictionary<string, string> properties,
            ILogger? logger = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            TlsClientOptions? tlsClientOptions = null,
            TlsServerOptions? tlsServerOptions = null)
            : this(ref args,
                   appSettings: null,
                   logger,
                   observer,
                   properties,
                   tlsClientOptions,
                   tlsServerOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="appSettings">Collection of settings to configure the new communicator properties. The
        /// appSettings param has precedence over the properties param.</param>
        /// <param name="logger">The logger used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the Ice run-time.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="tlsClientOptions">Client side configuration for TLS connections.</param>
        /// <param name="tlsServerOptions">Server side configuration for TLS connections.</param>
        public Communicator(
            NameValueCollection? appSettings = null,
            ILogger? logger = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            IReadOnlyDictionary<string, string>? properties = null,
            TlsClientOptions? tlsClientOptions = null,
            TlsServerOptions? tlsServerOptions = null)
            : this(ref _emptyArgs,
                   appSettings,
                   logger,
                   observer,
                   properties,
                   tlsClientOptions,
                   tlsServerOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="args">An array of command-line arguments used to set or override Ice.* properties.</param>
        /// <param name="appSettings">Collection of settings to configure the new communicator properties. The
        /// appSettings param has precedence over the properties param.</param>
        /// <param name="logger">The logger used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the new communicator.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="tlsClientOptions">Client side configuration for TLS connections.</param>
        /// <param name="tlsServerOptions">Server side configuration for TLS connections.</param>
        public Communicator(
            ref string[] args,
            NameValueCollection? appSettings = null,
            ILogger? logger = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            IReadOnlyDictionary<string, string>? properties = null,
            TlsClientOptions? tlsClientOptions = null,
            TlsServerOptions? tlsServerOptions = null)
        {
            _logger = logger ?? Runtime.Logger;
            Observer = observer;

            // clone properties as we don't want to modify the properties given to this constructor
            var combinedProperties =
                new Dictionary<string, string>(properties ?? ImmutableDictionary<string, string>.Empty);

            if (appSettings != null)
            {
                foreach (string? key in appSettings.AllKeys)
                {
                    if (key != null)
                    {
                        string[]? values = appSettings.GetValues(key);
                        if (values == null)
                        {
                            combinedProperties[key] = "";
                        }
                        else if (values.Length == 1)
                        {
                            combinedProperties[key] = values[0];
                        }
                        else
                        {
                            combinedProperties[key] = StringUtil.ToPropertyValue(values);
                        }
                    }
                }
            }

            if (!combinedProperties.ContainsKey("Ice.ProgramName"))
            {
                combinedProperties["Ice.ProgramName"] = AppDomain.CurrentDomain.FriendlyName;
            }

            combinedProperties.ParseIceArgs(ref args);
            SetProperties(combinedProperties);

            lock (_staticMutex)
            {
                if (!_oneOffDone)
                {
                    string? stdOut = GetProperty("Ice.StdOut");

                    System.IO.StreamWriter? outStream = null;

                    if (stdOut != null)
                    {
                        outStream = System.IO.File.AppendText(stdOut);
                        outStream.AutoFlush = true;
                        Console.Out.Close();
                        Console.SetOut(outStream);
                    }

                    string? stdErr = GetProperty("Ice.StdErr");
                    if (stdErr != null)
                    {
                        if (stdErr == stdOut)
                        {
                            Debug.Assert(outStream != null);
                            Console.SetError(outStream);
                        }
                        else
                        {
                            System.IO.StreamWriter errStream = System.IO.File.AppendText(stdErr);
                            errStream.AutoFlush = true;
                            Console.Error.Close();
                            Console.SetError(errStream);
                        }
                    }

                    UriParser.RegisterCommon();
                    _oneOffDone = true;
                }
            }

            if (logger == null)
            {
                string? logfile = GetProperty("Ice.LogFile");
                string? programName = GetProperty("Ice.ProgramName");
                Debug.Assert(programName != null);
                if (logfile != null)
                {
                    Logger = new FileLogger(programName, logfile);
                }
                else if (Runtime.Logger is Logger)
                {
                    // Ice.ConsoleListener is enabled by default.
                    Logger = new TraceLogger(programName, this.GetPropertyAsBool("Ice.ConsoleListener") ?? true);
                }
                // else already set to process logger
            }

            TraceLevels = new TraceLevels(this);

            DefaultInvocationTimeout =
                this.GetPropertyAsTimeSpan("Ice.Default.InvocationTimeout") ?? TimeSpan.FromSeconds(60);
            if (DefaultInvocationTimeout == TimeSpan.Zero)
            {
                throw new InvalidConfigurationException("0 is not a valid value for Ice.Default.InvocationTimeout");
            }

            DefaultPreferExistingConnection =
                this.GetPropertyAsBool("Ice.Default.PreferExistingConnection") ?? true;

            // TODO: switch to NonSecure.Never default
            DefaultPreferNonSecure =
                this.GetPropertyAsEnum<NonSecure>("Ice.Default.PreferNonSecure") ?? NonSecure.Always;

            if (GetProperty("Ice.Default.SourceAddress") is string address)
            {
                try
                {
                    DefaultSourceAddress = IPAddress.Parse(address);
                }
                catch (FormatException ex)
                {
                    throw new InvalidConfigurationException(
                        $"invalid IP address set for Ice.Default.SourceAddress: `{address}'", ex);
                }
            }

            // For locator cache timeout, 0 means disable locator cache.
            DefaultLocatorCacheTimeout =
                this.GetPropertyAsTimeSpan("Ice.Default.LocatorCacheTimeout") ?? Timeout.InfiniteTimeSpan;

            CloseTimeout = this.GetPropertyAsTimeSpan("Ice.CloseTimeout") ?? TimeSpan.FromSeconds(10);
            if (CloseTimeout == TimeSpan.Zero)
            {
                throw new InvalidConfigurationException("0 is not a valid value for Ice.CloseTimeout");
            }

            ConnectTimeout = this.GetPropertyAsTimeSpan("Ice.ConnectTimeout") ?? TimeSpan.FromSeconds(10);
            if (ConnectTimeout == TimeSpan.Zero)
            {
                throw new InvalidConfigurationException("0 is not a valid value for Ice.ConnectTimeout");
            }

            IdleTimeout = this.GetPropertyAsTimeSpan("Ice.IdleTimeout") ?? TimeSpan.FromSeconds(60);
            if (IdleTimeout == TimeSpan.Zero)
            {
                throw new InvalidConfigurationException("0 is not a valid value for Ice.IdleTimeout");
            }

            KeepAlive = this.GetPropertyAsBool("Ice.KeepAlive") ?? false;

            ServerName = GetProperty("Ice.ServerName") ?? Dns.GetHostName();

            MaxBidirectionalStreams = this.GetPropertyAsInt("Ice.MaxBidirectionalStreams") ?? 100;
            if (MaxBidirectionalStreams < 1)
            {
                throw new InvalidConfigurationException(
                    $"{MaxBidirectionalStreams} is not a valid value for Ice.MaxBidirectionalStreams");
            }

            MaxUnidirectionalStreams = this.GetPropertyAsInt("Ice.MaxUnidirectionalStreams") ?? 100;
            if (MaxUnidirectionalStreams < 1)
            {
                throw new InvalidConfigurationException(
                    $"{MaxBidirectionalStreams} is not a valid value for Ice.MaxUnidirectionalStreams");
            }

            SlicPacketMaxSize = this.GetPropertyAsByteSize("Ice.Slic.PacketMaxSize") ?? 32 * 1024;
            if (SlicPacketMaxSize < 1024)
            {
                throw new InvalidConfigurationException("Ice.Slic.PacketMaxSize can't be inferior to 1KB");
            }

            int frameMaxSize = this.GetPropertyAsByteSize("Ice.IncomingFrameMaxSize") ?? 1024 * 1024;
            IncomingFrameMaxSize = frameMaxSize == 0 ? int.MaxValue : frameMaxSize;
            if (IncomingFrameMaxSize < 1024)
            {
                throw new InvalidConfigurationException("Ice.IncomingFrameMaxSize can't be inferior to 1KB");
            }

            InvocationMaxAttempts = this.GetPropertyAsInt("Ice.InvocationMaxAttempts") ?? 5;

            if (InvocationMaxAttempts <= 0)
            {
                throw new InvalidConfigurationException($"Ice.InvocationMaxAttempts must be greater than 0");
            }
            InvocationMaxAttempts = Math.Min(InvocationMaxAttempts, 5);
            RetryBufferMaxSize = this.GetPropertyAsByteSize("Ice.RetryBufferMaxSize") ?? 1024 * 1024 * 100;
            RetryRequestMaxSize = this.GetPropertyAsByteSize("Ice.RetryRequestMaxSize") ?? 1024 * 1024;

            WarnConnections = this.GetPropertyAsBool("Ice.Warn.Connections") ?? false;
            WarnDatagrams = this.GetPropertyAsBool("Ice.Warn.Datagrams") ?? false;
            WarnDispatch = this.GetPropertyAsBool("Ice.Warn.Dispatch") ?? false;
            WarnUnknownProperties = this.GetPropertyAsBool("Ice.Warn.UnknownProperties") ?? true;

            CompressionLevel =
                this.GetPropertyAsEnum<CompressionLevel>("Ice.CompressionLevel") ?? CompressionLevel.Fastest;
            CompressionMinSize = this.GetPropertyAsByteSize("Ice.CompressionMinSize") ?? 100;

            // TODO: switch to NonSecure.Never default (see ObjectAdapter also)
            AcceptNonSecure = this.GetPropertyAsEnum<NonSecure>("Ice.AcceptNonSecure") ?? NonSecure.Always;
            int classGraphMaxDepth = this.GetPropertyAsInt("Ice.ClassGraphMaxDepth") ?? 100;
            ClassGraphMaxDepth = classGraphMaxDepth < 1 ? int.MaxValue : classGraphMaxDepth;

            ToStringMode = this.GetPropertyAsEnum<ToStringMode>("Ice.ToStringMode") ?? default;

            _backgroundLocatorCacheUpdates = this.GetPropertyAsBool("Ice.BackgroundLocatorCacheUpdates") ?? false;

            SslEngine = new SslEngine(this, tlsClientOptions, tlsServerOptions);

            RegisterIce1Transport(Transport.TCP,
                                  "tcp",
                                  TcpEndpoint.CreateIce1Endpoint,
                                  TcpEndpoint.ParseIce1Endpoint);

            RegisterIce1Transport(Transport.SSL,
                                  "ssl",
                                  TcpEndpoint.CreateIce1Endpoint,
                                  TcpEndpoint.ParseIce1Endpoint);

            RegisterIce1Transport(Transport.UDP,
                                  "udp",
                                  UdpEndpoint.CreateIce1Endpoint,
                                  UdpEndpoint.ParseIce1Endpoint);

            RegisterIce1Transport(Transport.WS,
                                  "ws",
                                  WSEndpoint.CreateIce1Endpoint,
                                  WSEndpoint.ParseIce1Endpoint);

            RegisterIce1Transport(Transport.WSS,
                                  "wss",
                                  WSEndpoint.CreateIce1Endpoint,
                                  WSEndpoint.ParseIce1Endpoint);

            RegisterIce2Transport(Transport.TCP,
                                  "tcp",
                                  TcpEndpoint.CreateIce2Endpoint,
                                  TcpEndpoint.ParseIce2Endpoint,
                                  IPEndpoint.DefaultIPPort);

            RegisterIce2Transport(Transport.WS,
                                  "ws",
                                  WSEndpoint.CreateIce2Endpoint,
                                  WSEndpoint.ParseIce2Endpoint,
                                  IPEndpoint.DefaultIPPort);

            if (this.GetPropertyAsBool("Ice.PreloadAssemblies") ?? false)
            {
                LoadAssemblies();
            }

            Observer?.SetObserverUpdater(new ObserverUpdater(this));

            try
            {
                _defaultLocator = this.GetPropertyAsProxy("Ice.Default.Locator", ILocatorPrx.Factory);
            }
            catch (FormatException ex)
            {
                throw new InvalidConfigurationException("invalid value for Ice.Default.Locator", ex);
            }

            // Show process id if requested (but only once).
            lock (_staticMutex)
            {
                if (!_printProcessIdDone && (this.GetPropertyAsBool("Ice.PrintProcessId") ?? false))
                {
                    using var p = System.Diagnostics.Process.GetCurrentProcess();
                    Console.WriteLine(p.Id);
                    _printProcessIdDone = true;
                }
            }
        }

        /// <summary>Activates the built-in locator implementation of this communicator, if any.</summary>
        /// <returns>A task that completes when the activation completes.</returns>
        public Task ActivateAsync() => Task.CompletedTask; // TODO: remove method

        /// <summary>Releases all resources used by this communicator. This method calls <see cref="ShutdownAsync"/>
        /// implicitly, and can be called multiple times.</summary>
        /// <returns>A task that completes when the destruction is complete.</returns>
        public Task DestroyAsync()
        {
            lock (_mutex)
            {
                _destroyTask ??= PerformDestroyAsync();
                return _destroyTask;
            }

            async Task PerformDestroyAsync()
            {
                // Cancel operations that are waiting and using the communicator's cancellation token
                _cancellationTokenSource.Cancel();

                // Shutdown and destroy all the incoming and outgoing Ice connections and wait for the connections to be
                // finished.
                var disposedException = new CommunicatorDisposedException();
                IEnumerable<Task> closeTasks =
                    _outgoingConnections.Values.SelectMany(connections => connections).Select(
                        connection => connection.GoAwayAsync(disposedException)).Append(ShutdownAsync());

                await Task.WhenAll(closeTasks).ConfigureAwait(false);

                foreach (Task<Connection> connect in _pendingOutgoingConnections.Values)
                {
                    try
                    {
                        Connection connection = await connect.ConfigureAwait(false);
                        await connection.GoAwayAsync(disposedException).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                // Ensure all the outgoing connections were removed
                Debug.Assert(_outgoingConnections.Count == 0);

                Observer?.SetObserverUpdater(null);

                if (this.GetPropertyAsBool("Ice.Warn.UnusedProperties") ?? false)
                {
                    List<string> unusedProperties = GetUnusedProperties();
                    if (unusedProperties.Count != 0)
                    {
                        var message = new StringBuilder("The following properties were set but never read:");
                        foreach (string s in unusedProperties)
                        {
                            message.Append("\n    ");
                            message.Append(s);
                        }
                        Logger.Warning(message.ToString());
                    }
                }

                if (Logger is FileLogger fileLogger)
                {
                    fileLogger.Dispose();
                }
                _currentContext.Dispose();
                _cancellationTokenSource.Dispose();
            }
        }

        /// <summary>An alias for <see cref="DestroyAsync"/>, except this method returns a <see cref="ValueTask"/>.
        /// </summary>
        /// <returns>A value task constructed using the task returned by DestroyAsync.</returns>
        public ValueTask DisposeAsync() => new(DestroyAsync());

        /// <summary>Registers a new transport for the ice1 protocol.</summary>
        /// <param name="transport">The transport.</param>
        /// <param name="transportName">The name of the transport in lower case, for example "tcp".</param>
        /// <param name="factory">A delegate that Ice will use to unmarshal endpoints for this transport.</param>
        /// <param name="parser">A delegate that Ice will use to parse endpoints for this transport.</param>
        public void RegisterIce1Transport(
            Transport transport,
            string transportName,
            Ice1EndpointFactory factory,
            Ice1EndpointParser parser)
        {
            if (transportName.Length == 0)
            {
                throw new ArgumentException($"{nameof(transportName)} cannot be empty", nameof(transportName));
            }

            _ice1TransportRegistry.Add(transport, factory);
            _ice1TransportNameRegistry.Add(transportName, (parser, transport));
        }

        /// <summary>Registers a new transport for the ice2 protocol.</summary>
        /// <param name="transport">The transport.</param>
        /// <param name="transportName">The name of the transport in lower case, for example "tcp".</param>
        /// <param name="factory">A delegate that Ice will use to unmarshal endpoints for this transport.</param>
        /// <param name="parser">A delegate that Ice will use to parse endpoints for this transport.</param>
        /// <param name="defaultPort">The default port for URI endpoints that don't specificy a port explicitly.</param>
        public void RegisterIce2Transport(
            Transport transport,
            string transportName,
            Ice2EndpointFactory factory,
            Ice2EndpointParser parser,
            ushort defaultPort)
        {
            if (transportName.Length == 0)
            {
                throw new ArgumentException($"{nameof(transportName)} cannot be empty", nameof(transportName));
            }

            _ice2TransportRegistry.Add(transport, (factory, parser));
            _ice2TransportNameRegistry.Add(transportName, (parser, transport));

            // Also register URI parser if not registered yet.
            try
            {
                UriParser.RegisterTransport(transportName, defaultPort);
            }
            catch (InvalidOperationException)
            {
                // Ignored, already registered
            }
        }

        internal void DecRetryBufferSize(int size)
        {
            lock (_mutex)
            {
                Debug.Assert(size <= _retryBufferSize);
                _retryBufferSize -= size;
            }
        }

        internal Ice1EndpointFactory? FindIce1EndpointFactory(Transport transport) =>
            _ice1TransportRegistry.TryGetValue(transport, out Ice1EndpointFactory? factory) ? factory : null;

        internal (Ice1EndpointParser Parser, Transport Transport)? FindIce1EndpointParser(string transportName) =>
            _ice1TransportNameRegistry.TryGetValue(
                transportName,
                out (Ice1EndpointParser, Transport) value) ? value : null;

        internal Ice2EndpointFactory? FindIce2EndpointFactory(Transport transport) =>
            _ice2TransportRegistry.TryGetValue(
                transport,
                out (Ice2EndpointFactory Factory, Ice2EndpointParser _) value) ? value.Factory : null;

        internal Ice2EndpointParser? FindIce2EndpointParser(Transport transport) =>
            _ice2TransportRegistry.TryGetValue(
                transport,
                out (Ice2EndpointFactory _, Ice2EndpointParser Parser) value) ? value.Parser : null;

        internal (Ice2EndpointParser Parser, Transport Transport)? FindIce2EndpointParser(string transportName) =>
            _ice2TransportNameRegistry.TryGetValue(
                transportName,
                out (Ice2EndpointParser, Transport) value) ? value : null;

        internal BufWarnSizeInfo GetBufWarnSize(Transport transport)
        {
            lock (_mutex)
            {
                BufWarnSizeInfo info;
                if (!_setBufWarnSize.ContainsKey(transport))
                {
                    info = new BufWarnSizeInfo();
                    info.SndWarn = false;
                    info.SndSize = -1;
                    info.RcvWarn = false;
                    info.RcvSize = -1;
                    _setBufWarnSize.Add(transport, info);
                }
                else
                {
                    info = _setBufWarnSize[transport];
                }
                return info;
            }
        }

        // Returns the IClassFactory associated with this Slice type ID, not null if not found.
        internal Func<AnyClass>? FindClassFactory(string typeId) =>
            _classFactoryCache.GetOrAdd(typeId, typeId =>
            {
                string className = TypeIdToClassName(typeId);
                Type? factoryClass = FindType($"ZeroC.Ice.ClassFactory.{className}");
                if (factoryClass != null)
                {
                    MethodInfo? method = factoryClass.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                    Debug.Assert(method != null);
                    return (Func<AnyClass>)Delegate.CreateDelegate(typeof(Func<AnyClass>), method);
                }
                return null;
            });

        internal Func<AnyClass>? FindClassFactory(int compactId) =>
           _compactIdCache.GetOrAdd(compactId, compactId =>
           {
               Type? factoryClass = FindType($"ZeroC.Ice.ClassFactory.CompactId_{compactId}");
               if (factoryClass != null)
               {
                   MethodInfo? method = factoryClass.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                   Debug.Assert(method != null);
                   return (Func<AnyClass>)Delegate.CreateDelegate(typeof(Func<AnyClass>), method);
               }
               return null;
           });

        internal Func<string?, RemoteExceptionOrigin?, RemoteException>? FindRemoteExceptionFactory(string typeId) =>
            _remoteExceptionFactoryCache.GetOrAdd(typeId, typeId =>
            {
                string className = TypeIdToClassName(typeId);
                Type? factoryClass = FindType($"ZeroC.Ice.RemoteExceptionFactory.{className}");
                if (factoryClass != null)
                {
                    MethodInfo? method = factoryClass.GetMethod(
                        "Create",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        CallingConventions.Any,
                        new Type[] { typeof(string), typeof(RemoteExceptionOrigin) },
                        null);
                    Debug.Assert(method != null);
                    return (Func<string?, RemoteExceptionOrigin?, RemoteException>)Delegate.CreateDelegate(
                        typeof(Func<string?, RemoteExceptionOrigin?, RemoteException>), method);
                }
                return null;
            });

        internal LocatorInfo? GetLocatorInfo(ILocatorPrx? locator)
        {
            // Returns locator info for a given locator. Automatically creates the locator info if it doesn't exist
            // yet.
            if (locator == null)
            {
                return null;
            }

            if (locator.Locator != null)
            {
                // The locator can't be located.
                locator = locator.Clone(clearLocator: true);
            }

            return _locatorInfoMap.GetOrAdd(locator,
                                            locator => new LocatorInfo(locator, _backgroundLocatorCacheUpdates));
        }

        internal bool IncRetryBufferSize(int size)
        {
            lock (_mutex)
            {
                if (size + _retryBufferSize < RetryBufferMaxSize)
                {
                    _retryBufferSize += size;
                    return true;
                }
            }
            return false;
        }
        internal void SetRcvBufWarnSize(Transport transport, int size)
        {
            lock (_mutex)
            {
                BufWarnSizeInfo info = GetBufWarnSize(transport);
                info.RcvWarn = true;
                info.RcvSize = size;
                _setBufWarnSize[transport] = info;
            }
        }

        internal void SetSndBufWarnSize(Transport transport, int size)
        {
            lock (_mutex)
            {
                BufWarnSizeInfo info = GetBufWarnSize(transport);
                info.SndWarn = true;
                info.SndSize = size;
                _setBufWarnSize[transport] = info;
            }
        }

        internal void UpdateConnectionObservers()
        {
            try
            {
                ObjectAdapter[] adapters;
                lock (_mutex)
                {
                    adapters = _adapters.ToArray();

                    foreach (ICollection<Connection> connections in _outgoingConnections.Values)
                    {
                        foreach (Connection c in connections)
                        {
                            c.UpdateObserver();
                        }
                    }
                }

                foreach (ObjectAdapter adapter in adapters)
                {
                    adapter.UpdateConnectionObservers();
                }
            }
            catch (CommunicatorDisposedException)
            {
            }
        }

        private static Type? FindType(string csharpId)
        {
            Type? t;
            LoadAssemblies(); // Lazy initialization
            foreach (Assembly a in _loadedAssemblies.Values)
            {
                if ((t = a.GetType(csharpId)) != null)
                {
                    return t;
                }
            }
            return null;
        }

        // Make sure that all assemblies that are referenced by this process are actually loaded. This is necessary so
        // we can use reflection on any type in any assembly because the type we are after will most likely not be in
        // the current assembly and, worse, may be in an assembly that has not been loaded yet. (Type.GetType() is no
        // good because it looks only in the calling object's assembly and mscorlib.dll.)
        private static void LoadAssemblies()
        {
            lock (_staticMutex)
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                var newAssemblies = new List<Assembly>();
                foreach (Assembly assembly in assemblies)
                {
                    if (!_loadedAssemblies.ContainsKey(assembly.FullName!))
                    {
                        newAssemblies.Add(assembly);
                        _loadedAssemblies[assembly.FullName!] = assembly;
                    }
                }

                foreach (Assembly a in newAssemblies)
                {
                    LoadReferencedAssemblies(a);
                }
            }
        }

        private static void LoadReferencedAssemblies(Assembly a)
        {
            try
            {
                AssemblyName[] names = a.GetReferencedAssemblies();
                foreach (AssemblyName name in names)
                {
                    if (!_loadedAssemblies.ContainsKey(name.FullName))
                    {
                        try
                        {
                            var loadedAssembly = Assembly.Load(name);
                            // The value of name.FullName may not match that of loadedAssembly.FullName, so we record
                            // the assembly using both keys.
                            _loadedAssemblies[name.FullName] = loadedAssembly;
                            _loadedAssemblies[loadedAssembly.FullName!] = loadedAssembly;
                            LoadReferencedAssemblies(loadedAssembly);
                        }
                        catch (Exception)
                        {
                            // Ignore assemblies that cannot be loaded.
                        }
                    }
                }
            }
            catch (PlatformNotSupportedException)
            {
                // Some platforms like UWP do not support using GetReferencedAssemblies
            }
        }

        private static string TypeIdToClassName(string typeId)
        {
            if (!typeId.StartsWith("::", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"`{typeId}' is not a valid Ice type ID");
            }
            return typeId[2..].Replace("::", ".");
        }
    }
}
