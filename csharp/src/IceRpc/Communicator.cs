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
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The central object in Ice. One or more communicators can be instantiated for an Ice application.
    /// </summary>
    public sealed partial class Communicator : IAsyncDisposable
    {
        /// <summary>The connection options.</summary>
        public OutgoingConnectionOptions ConnectionOptions;

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

        /// <summary>Gets the communicator observer used by the Ice run-time or null if a communicator observer
        /// was not set during communicator construction.</summary>
        public Instrumentation.ICommunicatorObserver? Observer { get; }

        /// <summary>The output mode or format for ToString on Ice proxies when the protocol is ice1. See
        /// <see cref="IceRpc.Interop.ToStringMode"/>.</summary>
        public ToStringMode ToStringMode { get; }

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
        /// <summary>Gets the maximum number of invocation attempts made to send a request including the original
        /// invocation. It must be a number greater than 0.</summary>
        internal int InvocationMaxAttempts { get; }
        internal bool IsDisposed => _shutdownTask != null;
        // TODO: should pass the factory and create a logger per locator client
        internal ILogger LocatorClientLogger { get; }
        /// <summary>The default logger for this communicator.</summary>
        internal ILogger Logger { get; }
        internal ILogger ProtocolLogger { get; }
        internal int RetryBufferMaxSize { get; }
        internal int RetryRequestMaxSize { get; }
        internal ILogger TransportLogger { get; }

        private static string[] _emptyArgs = Array.Empty<string>();

        private static bool _oneOffDone;

        private static bool _printProcessIdDone;

        private static readonly object _staticMutex = new();
        private readonly bool _backgroundLocatorCacheUpdates;
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        private readonly ThreadLocal<SortedDictionary<string, string>> _currentContext = new();
        private Task? _shutdownTask;

        private readonly object _mutex = new();

        private int _retryBufferSize;

        private readonly IDictionary<Transport, (EndpointFactory Factory, Ice1EndpointFactory? Ice1Factory, Ice1EndpointParser? Ice1Parser, Ice2EndpointParser? Ice2Parser)> _transportRegistry =
            new ConcurrentDictionary<Transport, (EndpointFactory, Ice1EndpointFactory?, Ice1EndpointParser?, Ice2EndpointParser?)>();

        private readonly IDictionary<string, (Ice1EndpointParser? Ice1Parser, Ice2EndpointParser? Ice2Parser, Transport Transport)> _transportNameRegistry =
            new ConcurrentDictionary<string, (Ice1EndpointParser?, Ice2EndpointParser?, Transport)>();

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="loggerFactory">The logger factory used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the new communicator.</param>
        /// <param name="connectionOptions">Connection options.</param>
        public Communicator(
            IReadOnlyDictionary<string, string> properties,
            ILoggerFactory? loggerFactory = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            OutgoingConnectionOptions? connectionOptions = null)
            : this(ref _emptyArgs,
                   appSettings: null,
                   loggerFactory,
                   observer,
                   properties,
                   connectionOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="args">An array of command-line arguments used to set or override Ice.* properties.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="loggerFactory">The logger factory used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the new communicator.</param>
        /// <param name="connectionOptions">Connection options.</param>
        public Communicator(
            ref string[] args,
            IReadOnlyDictionary<string, string> properties,
            ILoggerFactory? loggerFactory = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            OutgoingConnectionOptions? connectionOptions = null)
            : this(ref args,
                   appSettings: null,
                   loggerFactory,
                   observer,
                   properties,
                   connectionOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="appSettings">Collection of settings to configure the new communicator properties. The
        /// appSettings param has precedence over the properties param.</param>
        /// <param name="loggerFactory">The logger factory used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the Ice run-time.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="connectionOptions">Connection options.</param>
        public Communicator(
            NameValueCollection? appSettings = null,
            ILoggerFactory? loggerFactory = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            IReadOnlyDictionary<string, string>? properties = null,
            OutgoingConnectionOptions? connectionOptions = null)
            : this(ref _emptyArgs,
                   appSettings,
                   loggerFactory,
                   observer,
                   properties,
                   connectionOptions)
        {
        }

        /// <summary>Constructs a new communicator.</summary>
        /// <param name="args">An array of command-line arguments used to set or override Ice.* properties.</param>
        /// <param name="appSettings">Collection of settings to configure the new communicator properties. The
        /// appSettings param has precedence over the properties param.</param>
        /// <param name="loggerFactory">The loggerFactory used by the new communicator.</param>
        /// <param name="observer">The communicator observer used by the new communicator.</param>
        /// <param name="properties">The properties of the new communicator.</param>
        /// <param name="connectionOptions">Connection options.</param>
        public Communicator(
            ref string[] args,
            NameValueCollection? appSettings = null,
            ILoggerFactory? loggerFactory = null,
            Instrumentation.ICommunicatorObserver? observer = null,
            IReadOnlyDictionary<string, string>? properties = null,
            OutgoingConnectionOptions? connectionOptions = null)
        {
            loggerFactory ??= NullLoggerFactory.Instance;
            Logger = loggerFactory.CreateLogger("IceRpc");
            LocatorClientLogger = loggerFactory.CreateLogger("IceRpc.Interop.LocatorClient");

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

            ProtocolLogger = loggerFactory.CreateLogger("IceRpc.Protocol");
            TransportLogger = loggerFactory.CreateLogger("IceRpc.Transport");

            ConnectionOptions = connectionOptions?.Clone() ?? new OutgoingConnectionOptions();
            ConnectionOptions.SocketOptions ??= new SocketOptions();
            ConnectionOptions.SlicOptions ??= new SlicOptions();

            // TODO: remove once old tests which rely on properties are removed
            var socketOptions = ConnectionOptions.SocketOptions!;
            socketOptions.ReceiveBufferSize =
                this.GetPropertyAsByteSize($"Ice.UDP.RcvSize") ?? socketOptions.ReceiveBufferSize;
            socketOptions.SendBufferSize =
                this.GetPropertyAsByteSize($"Ice.UDP.SndSize") ?? socketOptions.SendBufferSize;
            socketOptions.ReceiveBufferSize =
                this.GetPropertyAsByteSize($"Ice.TCP.RcvSize") ?? socketOptions.ReceiveBufferSize;
            socketOptions.SendBufferSize =
                this.GetPropertyAsByteSize($"Ice.TCP.SndSize") ?? socketOptions.SendBufferSize;
            ConnectionOptions.IncomingFrameMaxSize =
                this.GetPropertyAsByteSize("Ice.IncomingFrameMaxSize") ?? ConnectionOptions.IncomingFrameMaxSize;

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
        /// <param name="ice1Factory">A delegate that creates an ice1 endpoint by reading an <see cref="InputStream"/>
        /// (optional).</param>
        /// <param name="ice1Parser">A delegate that creates an ice1 endpoint from a pre-parsed string.</param>
        /// <param name="ice2Parser">A delegate that creates an ice2 endpoint from a pre-parsed URI.</param>
        /// <param name="defaultUriPort">The default port for URI endpoints that don't specify a port explicitly.
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
    }
}
