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
        internal int RetryBufferMaxSize { get; }
        internal int RetryRequestMaxSize { get; }

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

            Logger = loggerFactory.CreateLogger("IceRpc");

            ConnectionOptions = connectionOptions?.Clone() ?? new OutgoingConnectionOptions();
            ConnectionOptions.TransportOptions ??= new TcpOptions();

            // TODO: remove once old tests which rely on properties are removed
            if (ConnectionOptions.TransportOptions is UdpOptions udpOptions)
            {
                udpOptions.ReceiveBufferSize =
                    this.GetPropertyAsByteSize($"Ice.UDP.RcvSize") ?? udpOptions.ReceiveBufferSize;
                udpOptions.SendBufferSize =
                    this.GetPropertyAsByteSize($"Ice.UDP.SndSize") ?? udpOptions.SendBufferSize;
            }
            else if(ConnectionOptions.TransportOptions is TcpOptions tcpOptions)
            {
                tcpOptions.ReceiveBufferSize =
                    this.GetPropertyAsByteSize($"Ice.TCP.RcvSize") ?? tcpOptions.ReceiveBufferSize;
                tcpOptions.SendBufferSize =
                    this.GetPropertyAsByteSize($"Ice.TCP.SndSize") ?? tcpOptions.SendBufferSize;
            }

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

        internal void DecRetryBufferSize(int size)
        {
            lock (_mutex)
            {
                Debug.Assert(size <= _retryBufferSize);
                _retryBufferSize -= size;
            }
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
    }
}
