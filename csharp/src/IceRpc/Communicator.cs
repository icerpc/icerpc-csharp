// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
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

        /// <summary>The default invocation interceptors for proxies created using this communicator. Changing the value
        /// of DefaultInvocationInterceptors does not change the invocation interceptors of previously created proxies.
        /// </summary>
        public ImmutableList<InvocationInterceptor> DefaultInvocationInterceptors
        {
            get => _defaultInvocationInterceptors;
            set => _defaultInvocationInterceptors = value;
        }

        /// <summary>The default location resolver for this communicator.</summary>
        public ILocationResolver? DefaultLocationResolver
        {
            get => _defaultLocationResolver;
            set => _defaultLocationResolver = value;
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

        /// <summary>Gets the communicator observer used by the Ice run-time or null if a communicator observer
        /// was not set during communicator construction.</summary>
        public Instrumentation.ICommunicatorObserver? Observer { get; }

        /// <summary>The output mode or format for ToString on Ice proxies when the protocol is ice1. See
        /// <see cref="IceRpc.Interop.ToStringMode"/>.</summary>
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
        internal bool IsDisposed => _shutdownTask != null;
        internal bool KeepAlive { get; }

        /// <summary>The default logger for this communicator.</summary>
        internal ILogger Logger { get; }

        // TODO: should pass the factory and create a logger per locator client
         internal ILogger LocatorClientLogger { get; }
        internal ILoggerFactory LoggerFactory { get; }
        internal int MaxBidirectionalStreams { get; }
        internal int MaxUnidirectionalStreams { get; }
        internal int SlicPacketMaxSize { get; }
        internal int SlicStreamBufferMaxSize { get; }

        /// <summary>Gets the maximum number of invocation attempts made to send a request including the original
        /// invocation. It must be a number greater than 0.</summary>
        internal int InvocationMaxAttempts { get; }
        internal ILogger ProtocolLogger { get; }
        internal int RetryBufferMaxSize { get; }
        internal int RetryRequestMaxSize { get; }
        internal ILogger SecurityLogger { get; }
        internal SslClientAuthenticationOptions? AuthenticationOptions { get; }
        internal ILogger TransportLogger { get; }
        internal bool WarnConnections { get; }
        internal bool WarnDatagrams { get; }
        internal bool WarnUnknownProperties { get; }

        private static string[] _emptyArgs = Array.Empty<string>();

        private static readonly Dictionary<string, Assembly> _loadedAssemblies = new();

        private static bool _oneOffDone;

        private static bool _printProcessIdDone;

        private static readonly object _staticMutex = new object();
        private readonly bool _backgroundLocatorCacheUpdates;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ConcurrentDictionary<string, Func<AnyClass>?> _classFactoryCache = new();
        private readonly ConcurrentDictionary<int, Func<AnyClass>?> _compactIdCache = new();
        private readonly ThreadLocal<SortedDictionary<string, string>> _currentContext = new();
        private volatile ImmutableSortedDictionary<string, string> _defaultContext =
            ImmutableSortedDictionary<string, string>.Empty;
        private volatile ImmutableList<InvocationInterceptor> _defaultInvocationInterceptors =
            ImmutableList<InvocationInterceptor>.Empty;
        private volatile ILocationResolver? _defaultLocationResolver;
        private Task? _shutdownTask;

        private readonly object _mutex = new object();

        private readonly ConcurrentDictionary<string, Func<string?, RemoteExceptionOrigin, RemoteException>?> _remoteExceptionFactoryCache =
            new();
        private int _retryBufferSize;

        private readonly Dictionary<Transport, BufWarnSizeInfo> _setBufWarnSize = new();

        private readonly IDictionary<Transport, (EndpointFactory Factory, Ice1EndpointFactory? Ice1Factory, Ice1EndpointParser? Ice1Parser, Ice2EndpointParser? Ice2Parser)> _transportRegistry =
            new ConcurrentDictionary<Transport, (EndpointFactory, Ice1EndpointFactory?, Ice1EndpointParser?, Ice2EndpointParser?)>();

        private readonly IDictionary<string, (Ice1EndpointParser? Ice1Parser, Ice2EndpointParser? Ice2Parser, Transport Transport)> _transportNameRegistry =
            new ConcurrentDictionary<string, (Ice1EndpointParser?, Ice2EndpointParser?, Transport)>();

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="loggerFactory">The logger factory used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the new communicator.</param>
        /// <param name="authenticationOptions">Client side options for authentication of SSL connections.</param>
        public Communicator(
            IReadOnlyDictionary<string, string> properties,
            ILoggerFactory? loggerFactory = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            SslClientAuthenticationOptions? authenticationOptions = null)
            : this(ref _emptyArgs,
                   appSettings: null,
                   loggerFactory,
                   observer,
                   properties,
                   authenticationOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="args">An array of command-line arguments used to set or override Ice.* properties.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="loggerFactory">The logger factory used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the new communicator.</param>
        /// <param name="authenticationOptions">Client side options for authentication of SSL connections.</param>
        public Communicator(
            ref string[] args,
            IReadOnlyDictionary<string, string> properties,
            ILoggerFactory? loggerFactory = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            SslClientAuthenticationOptions? authenticationOptions = null)
            : this(ref args,
                   appSettings: null,
                   loggerFactory,
                   observer,
                   properties,
                   authenticationOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="appSettings">Collection of settings to configure the new communicator properties. The
        /// appSettings param has precedence over the properties param.</param>
        /// <param name="loggerFactory">The logger factory used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the Ice run-time.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="authenticationOptions">Client side options for authentication of SSL connections.</param>
        public Communicator(
            NameValueCollection? appSettings = null,
            ILoggerFactory? loggerFactory = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            IReadOnlyDictionary<string, string>? properties = null,
            SslClientAuthenticationOptions? authenticationOptions = null)
            : this(ref _emptyArgs,
                   appSettings,
                   loggerFactory,
                   observer,
                   properties,
                   authenticationOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="args">An array of command-line arguments used to set or override Ice.* properties.</param>
        /// <param name="appSettings">Collection of settings to configure the new communicator properties. The
        /// appSettings param has precedence over the properties param.</param>
        /// <param name="loggerFactory">The loggerFactory used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the new communicator.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="authenticationOptions">Client side options for authentication of SSL connections.</param>
        public Communicator(
            ref string[] args,
            NameValueCollection? appSettings = null,
            ILoggerFactory? loggerFactory = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            IReadOnlyDictionary<string, string>? properties = null,
            SslClientAuthenticationOptions? authenticationOptions = null)
        {
            LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            Logger = LoggerFactory.CreateLogger("IceRpc");
            LocatorClientLogger = LoggerFactory.CreateLogger("IceRpc.Interop.LocatorClient");
            TransportLogger = LoggerFactory.CreateLogger("IceRpc.Transport");
            ProtocolLogger = LoggerFactory.CreateLogger("IceRpc.Protocol");
            SecurityLogger = LoggerFactory.CreateLogger("IceRpc.Security");
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
                    UriParser.RegisterCommon();
                    _oneOffDone = true;
                }
            }

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

            SlicStreamBufferMaxSize =
                this.GetPropertyAsByteSize("Ice.Slic.StreamBufferMaxSize") ?? 2 * SlicPacketMaxSize;
            if (SlicStreamBufferMaxSize < SlicPacketMaxSize)
            {
                throw new InvalidConfigurationException(
                    "Ice.Slic.StreamBufferMaxSize can't be inferior to Ice.Slic.PacketMaxSize");
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

            CompressionLevel =
                this.GetPropertyAsEnum<CompressionLevel>("Ice.CompressionLevel") ?? CompressionLevel.Fastest;
            CompressionMinSize = this.GetPropertyAsByteSize("Ice.CompressionMinSize") ?? 100;

            int classGraphMaxDepth = this.GetPropertyAsInt("Ice.ClassGraphMaxDepth") ?? 100;
            ClassGraphMaxDepth = classGraphMaxDepth < 1 ? int.MaxValue : classGraphMaxDepth;

            ToStringMode = this.GetPropertyAsEnum<ToStringMode>("Ice.ToStringMode") ?? default;

            _backgroundLocatorCacheUpdates = this.GetPropertyAsBool("Ice.BackgroundLocatorCacheUpdates") ?? false;

            if (authenticationOptions != null)
            {
                AuthenticationOptions = new SslClientAuthenticationOptions()
                {
                    AllowRenegotiation = authenticationOptions.AllowRenegotiation,
                    ApplicationProtocols = authenticationOptions.ApplicationProtocols,
                    CertificateRevocationCheckMode = authenticationOptions.CertificateRevocationCheckMode,
                    CipherSuitesPolicy = authenticationOptions.CipherSuitesPolicy,
                    ClientCertificates = authenticationOptions.ClientCertificates,
                    EnabledSslProtocols = authenticationOptions.EnabledSslProtocols,
                    EncryptionPolicy = authenticationOptions.EncryptionPolicy,
                    LocalCertificateSelectionCallback = authenticationOptions.LocalCertificateSelectionCallback,
                    RemoteCertificateValidationCallback = authenticationOptions.RemoteCertificateValidationCallback,
                    TargetHost = authenticationOptions.TargetHost
                };
            }

            RegisterTransport(Transport.Loc,
                              "loc",
                              LocEndpoint.Create,
                              ice2Parser: LocEndpoint.ParseIce2Endpoint,
                              defaultUriPort: LocEndpoint.DefaultLocPort);

            RegisterTransport(Transport.TCP,
                              "tcp",
                              TcpEndpoint.CreateEndpoint,
                              TcpEndpoint.CreateIce1Endpoint,
                              TcpEndpoint.ParseIce1Endpoint,
                              TcpEndpoint.ParseIce2Endpoint,
                              IPEndpoint.DefaultIPPort);

            RegisterTransport(Transport.SSL,
                              "ssl",
                              TcpEndpoint.CreateEndpoint,
                              TcpEndpoint.CreateIce1Endpoint,
                              TcpEndpoint.ParseIce1Endpoint);

            RegisterTransport(Transport.UDP,
                              "udp",
                              UdpEndpoint.CreateEndpoint,
                              UdpEndpoint.CreateIce1Endpoint,
                              UdpEndpoint.ParseIce1Endpoint);

            RegisterTransport(Transport.WS,
                              "ws",
                              WSEndpoint.CreateEndpoint,
                              WSEndpoint.CreateIce1Endpoint,
                              WSEndpoint.ParseIce1Endpoint,
                              WSEndpoint.ParseIce2Endpoint,
                              IPEndpoint.DefaultIPPort);

            RegisterTransport(Transport.WSS,
                              "wss",
                              WSEndpoint.CreateEndpoint,
                              WSEndpoint.CreateIce1Endpoint,
                              WSEndpoint.ParseIce1Endpoint);

            if (this.GetPropertyAsBool("Ice.PreloadAssemblies") ?? false)
            {
                LoadAssemblies();
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

        /// <summary>Releases all resources used by this communicator. This method can be called multiple times.
        /// </summary>
        /// <returns>A task that completes when the destruction is complete.</returns>
        // TODO: add cancellation token, switch to lazy task pattern
        public Task ShutdownAsync()
        {
            lock (_mutex)
            {
                _shutdownTask ??= PerformShutdownAsync();
                return _shutdownTask;
            }

            async Task PerformShutdownAsync()
            {
                // Cancel operations that are waiting and using the communicator's cancellation token
                _cancellationTokenSource.Cancel();

                // Shutdown and destroy all the incoming and outgoing Ice connections and wait for the connections to be
                // finished.
                var disposedException = new CommunicatorDisposedException();
                IEnumerable<Task> closeTasks =
                    _outgoingConnections.Values.SelectMany(connections => connections).Select(
                        connection => connection.GoAwayAsync(disposedException));

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
                _currentContext.Dispose();
                _cancellationTokenSource.Dispose();
            }
        }

        /// <summary>An alias for <see cref="ShutdownAsync"/>, except this method returns a <see cref="ValueTask"/>.
        /// </summary>
        /// <returns>A value task constructed using the task returned by ShutdownAsync.</returns>
        public ValueTask DisposeAsync() => new(ShutdownAsync());

        /// <summary>Registers a new transport.</summary>
        /// <param name="transport">The transport.</param>
        /// <param name="transportName">The name of the transport in lower case, for example "tcp".</param>
        /// <param name="factory">A delegate that creates an endpoint from an <see cref="EndpointData"/>.</param>
        /// <param name="ice1Factory">A delegate creates an ice1 endpoint by reading an <see cref="InputStream"/>
        /// (optional).</param>
        /// <param name="ice1Parser">A delegate that creates an ice1 endpoint from a pre-parsed string.</param>
        /// <param name="ice2Parser">A delegate that creates an ice2 endpoint from a pre-parsed URI.</param>
        /// <param name="defaultUriPort">The default port for URI endpoints that don't specificy a port explicitly.
        /// </param>
        public void RegisterTransport(
            Transport transport,
            string transportName,
            EndpointFactory factory,
            Ice1EndpointFactory? ice1Factory = null,
            Ice1EndpointParser? ice1Parser = null,
            Ice2EndpointParser? ice2Parser = null,
            ushort defaultUriPort = 0)
        {
            if (transportName.Length == 0)
            {
                throw new ArgumentException($"{nameof(transportName)} cannot be empty", nameof(transportName));
            }

            if (ice1Factory != null && ice1Parser == null)
            {
                throw new ArgumentNullException($"{nameof(ice1Parser)} cannot be null", nameof(ice1Parser));
            }

            if (ice1Factory == null && ice2Parser == null)
            {
                throw new ArgumentNullException($"{nameof(ice2Parser)} cannot be null", nameof(ice2Parser));
            }

            _transportRegistry.Add(transport, (factory, ice1Factory, ice1Parser, ice2Parser));
            _transportNameRegistry.Add(transportName, (ice1Parser, ice2Parser, transport));

            if (ice2Parser != null)
            {
                // Also register URI parser if not registered yet.
                try
                {
                    UriParser.RegisterTransport(transportName, defaultUriPort);
                }
                catch (InvalidOperationException)
                {
                    // Ignored, already registered
                }
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

        internal EndpointFactory? FindEndpointFactory(Transport transport) =>
            _transportRegistry.TryGetValue(transport, out var value) ? value.Factory : null;

        internal (Ice1EndpointParser?, Ice2EndpointParser?, Transport)? FindEndpointParser(
            string transportName) =>
            _transportNameRegistry.TryGetValue(transportName, out var value) ? value : null;

        internal Ice1EndpointFactory? FindIce1EndpointFactory(Transport transport) =>
            _transportRegistry.TryGetValue(transport, out var value) ? value.Ice1Factory : null;

        internal (Ice1EndpointParser, Transport)? FindIce1EndpointParser(string transportName) =>
            _transportNameRegistry.TryGetValue(transportName, out var value) && value.Ice1Parser != null ?
                (value.Ice1Parser, value.Transport) : null;

        internal (Ice2EndpointParser, Transport)? FindIce2EndpointParser(string transportName) =>
            _transportNameRegistry.TryGetValue(transportName, out var value) && value.Ice2Parser != null ?
                (value.Ice2Parser, value.Transport) : null;

        internal Ice2EndpointParser? FindIce2EndpointParser(Transport transport) =>
            _transportRegistry.TryGetValue(transport, out var value) ? value.Ice2Parser : null;

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
                Type? factoryClass = FindType($"IceRpc.ClassFactory.{className}");
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
               Type? factoryClass = FindType($"IceRpc.ClassFactory.CompactId_{compactId}");
               if (factoryClass != null)
               {
                   MethodInfo? method = factoryClass.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                   Debug.Assert(method != null);
                   return (Func<AnyClass>)Delegate.CreateDelegate(typeof(Func<AnyClass>), method);
               }
               return null;
           });

        internal Func<string?, RemoteExceptionOrigin, RemoteException>? FindRemoteExceptionFactory(string typeId) =>
            _remoteExceptionFactoryCache.GetOrAdd(typeId, typeId =>
            {
                string className = TypeIdToClassName(typeId);
                Type? factoryClass = FindType($"IceRpc.RemoteExceptionFactory.{className}");
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
                    return (Func<string?, RemoteExceptionOrigin, RemoteException>)Delegate.CreateDelegate(
                        typeof(Func<string?, RemoteExceptionOrigin, RemoteException>), method);
                }
                return null;
            });

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
                        catch
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
