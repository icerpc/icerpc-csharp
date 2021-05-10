// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
[assembly: InternalsVisibleTo("IceRpc.Tests.Api")]
[assembly: InternalsVisibleTo("IceRpc.Tests.CodeGeneration")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Internal")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Encoding")]
[assembly: InternalsVisibleTo("IceRpc.Tests.ClientServer")]

namespace IceRpc
{
    /// <summary>Provides global configuration for IceRPC in the current process.</summary>
    public static class Runtime
    {
        /// <summary>The IceRPC version in semver format.</summary>
        public const string StringVersion = "0.0.1-alpha";

        /// <summary>Gets or sets the logger factory used by IceRPC classes when no logger factory is explicitly
        /// configured.</summary>
        public static ILoggerFactory DefaultLoggerFactory { get; set; } = NullLoggerFactory.Instance;

        internal static IReadOnlyDictionary<int, Lazy<ClassFactory>> CompactTypeIdClassFactoryDictionary
        {
            get
            {
                if (_compactTypeIdClassFactoryCache == null)
                {
                    RegisterClassFactoriesFromAllAssemblies();
                }
                Debug.Assert(_compactTypeIdClassFactoryCache != null);
                return _compactTypeIdClassFactoryCache;
            }
        }

        internal static IReadOnlyDictionary<string, Lazy<ClassFactory>> TypeIdClassFactoryDictionary
        {
            get
            {
                if (_typeIdClassFactoryCache == null)
                {
                    RegisterClassFactoriesFromAllAssemblies();
                }
                Debug.Assert(_typeIdClassFactoryCache != null);
                return _typeIdClassFactoryCache;
            }
        }

        internal static IReadOnlyDictionary<string, Lazy<RemoteExceptionFactory>> TypeIdRemoteExceptionFactoryDictionary
        {
            get
            {
                if (_typeIdRemoteExceptionFactoryCache == null)
                {
                    RegisterClassFactoriesFromAllAssemblies();
                }
                Debug.Assert(_typeIdRemoteExceptionFactoryCache != null);
                return _typeIdRemoteExceptionFactoryCache;
            }
        }

        private static IReadOnlyDictionary<int, Lazy<ClassFactory>>? _compactTypeIdClassFactoryCache;

        // The mutex protects assignment to class and exception factory caches
        private static readonly object _mutex = new();
        private static IReadOnlyDictionary<string, Lazy<ClassFactory>>? _typeIdClassFactoryCache;
        private static IReadOnlyDictionary<string, Lazy<RemoteExceptionFactory>>? _typeIdRemoteExceptionFactoryCache;

        private static readonly IDictionary<string, (Ice1EndpointParser? Ice1Parser, Ice2EndpointParser? Ice2Parser, Transport Transport)> _transportNameRegistry =
            new ConcurrentDictionary<string, (Ice1EndpointParser?, Ice2EndpointParser?, Transport)>();

        private static readonly IDictionary<Transport, (EndpointFactory Factory, Ice1EndpointFactory? Ice1Factory, Ice1EndpointParser? Ice1Parser, Ice2EndpointParser? Ice2Parser)> _transportRegistry =
            new ConcurrentDictionary<Transport, (EndpointFactory, Ice1EndpointFactory?, Ice1EndpointParser?, Ice2EndpointParser?)>();

        /// <summary>Register class and exceptions factories found in the given assembly.
        /// <seealso cref="RegisterClassFactoriesFromAllAssemblies"/>.</summary>
        public static void RegisterClassFactoriesFromAssembly(Assembly assembly)
        {
            var loadedAssemblies = new HashSet<Assembly>();
            LoadReferencedAssemblies(assembly, loadedAssemblies);
            RegisterClassFactories(assembly.GetCustomAttributes<ClassAttribute>());
        }

        /// <summary>Registers class and exception factories found in the current executing assembly and in all
        /// assemblies referenced by it. If this method is not explicitly called an no class factories where previously
        /// registered by calling <see cref="RegisterClassFactoriesFromAssembly(Assembly)"/> it is called implicitly the
        /// first time IceRPC looks up a class or exception factory.</summary>
        public static void RegisterClassFactoriesFromAllAssemblies()
        {
            var loadedAssemblies = new HashSet<Assembly>();
            foreach (Assembly assembly in AssemblyLoadContext.Default.Assemblies)
            {
                LoadReferencedAssemblies(assembly, loadedAssemblies);
            }
            RegisterClassFactories(
                loadedAssemblies.SelectMany(assembly => assembly.GetCustomAttributes<ClassAttribute>()));
        }

        /// <summary>Registers a new transport.</summary>
        /// <param name="transport">The transport.</param>
        /// <param name="transportName">The name of the transport in lower case, for example "tcp".</param>
        /// <param name="factory">A delegate that creates an endpoint from an <see cref="EndpointData"/>.</param>
        /// <param name="ice1Factory">A delegate that creates an ice1 endpoint by reading an <see cref="InputStream"/>.
        /// </param>
        /// <param name="ice1Parser">A delegate that creates an ice1 endpoint from a pre-parsed string.</param>
        /// <param name="ice2Parser">A delegate that creates an ice2 endpoint from a pre-parsed URI.</param>
        /// <param name="defaultUriPort">The default port for URI endpoints that don't specify a port explicitly.
        /// </param>
        public static void RegisterTransport(
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
                Internal.UriParser.RegisterTransport(transportName, defaultUriPort);
            }
        }

        static Runtime()
        {
            // Register the ice and ice+universal schemes with the system UriParser.
            Internal.UriParser.RegisterTransport("universal", UniversalEndpoint.DefaultUniversalPort);
            Internal.UriParser.RegisterIceScheme();

            RegisterTransport(Transport.Loc,
                              "loc",
                              LocEndpoint.Create,
                              ice1Parser: LocEndpoint.ParseIce1Endpoint,
                              ice2Parser: LocEndpoint.ParseIce2Endpoint,
                              defaultUriPort: LocEndpoint.DefaultLocPort);

            RegisterTransport(Transport.Coloc,
                              "coloc",
                              ColocEndpoint.CreateEndpoint,
                              ice1Parser: ColocEndpoint.ParseIce1Endpoint,
                              ice2Parser: ColocEndpoint.ParseIce2Endpoint,
                              defaultUriPort: ColocEndpoint.DefaultColocPort);

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
        }

        // Must be called before parsing a Uri to make sure Runtime's static constructor executed and registered the
        // URI schemes for the built-in transports.
        internal static void UriInitialize()
        {
            if (_transportRegistry.Count == 0)
            {
                // should never happen
                throw new InvalidOperationException("transports are not yet registered");
            }
        }

        internal static EndpointFactory? FindEndpointFactory(Transport transport) =>
            _transportRegistry.TryGetValue(transport, out var value) ? value.Factory : null;

        internal static Ice1EndpointFactory? FindIce1EndpointFactory(Transport transport) =>
            _transportRegistry.TryGetValue(transport, out var value) ? value.Ice1Factory : null;

        internal static (Ice1EndpointParser, Transport)? FindIce1EndpointParser(string transportName) =>
            _transportNameRegistry.TryGetValue(transportName, out var value) && value.Ice1Parser != null ?
                (value.Ice1Parser, value.Transport) : null;

        internal static (Ice2EndpointParser, Transport)? FindIce2EndpointParser(string transportName) =>
            _transportNameRegistry.TryGetValue(transportName, out var value) && value.Ice2Parser != null ?
                (value.Ice2Parser, value.Transport) : null;

        internal static Ice2EndpointParser? FindIce2EndpointParser(Transport transport) =>
            _transportRegistry.TryGetValue(transport, out var value) ? value.Ice2Parser : null;

        private static void LoadReferencedAssemblies(Assembly entryAssembly, HashSet<Assembly> seenAssembly)
        {
            if (seenAssembly.Add(entryAssembly))
            {
                foreach (AssemblyName name in entryAssembly.GetReferencedAssemblies())
                {
                    try
                    {
                        LoadReferencedAssemblies(AssemblyLoadContext.Default.LoadFromAssemblyName(name), seenAssembly);
                    }
                    catch
                    {
                        // Ignore assemblies that cannot be loaded.
                    }
                }
            }
        }

        private static void RegisterClassFactories(IEnumerable<ClassAttribute> attributes)
        {
            Dictionary<string, Lazy<ClassFactory>>? typeIdClassFactories = null;
            Dictionary<int, Lazy<ClassFactory>>? compactIdClassFactories = null;
            Dictionary<string, Lazy<RemoteExceptionFactory>>? typeIdRemoteExceptionFactories = null;

            foreach (ClassAttribute attribute in attributes)
            {
                if (typeof(AnyClass).IsAssignableFrom(attribute.Type))
                {
                    typeIdClassFactories ??= new Dictionary<string, Lazy<ClassFactory>>();
                    var factory = new Lazy<ClassFactory>(() => attribute.ClassFactory);
                    if (attribute.CompactTypeId is int compactTypeId)
                    {
                        compactIdClassFactories ??= new Dictionary<int, Lazy<ClassFactory>>();
                        compactIdClassFactories[compactTypeId] = factory;
                    }
                    typeIdClassFactories[attribute.TypeId] = factory;
                }
                else
                {
                    Debug.Assert(typeof(RemoteException).IsAssignableFrom(attribute.Type));
                    var factory = new Lazy<RemoteExceptionFactory>(() => attribute.ExceptionFactory);
                    typeIdRemoteExceptionFactories ??= new Dictionary<string, Lazy<RemoteExceptionFactory>>();
                    typeIdRemoteExceptionFactories[attribute.TypeId] = factory;
                }
            }

            lock (_mutex)
            {
                if (typeIdClassFactories != null)
                {
                    Dictionary<string, Lazy<ClassFactory>> typeIdClassFactoryCache = _typeIdClassFactoryCache == null ?
                        new Dictionary<string, Lazy<ClassFactory>>() :
                        new Dictionary<string, Lazy<ClassFactory>>(_typeIdClassFactoryCache);
                    foreach ((string type, Lazy<ClassFactory> factory) in typeIdClassFactories)
                    {
                        typeIdClassFactoryCache[type] = factory;
                    }
                    _typeIdClassFactoryCache = typeIdClassFactoryCache;
                }

                if (compactIdClassFactories != null)
                {
                    Dictionary<int, Lazy<ClassFactory>> compactIdClassFactoryCache =
                        _compactTypeIdClassFactoryCache == null ?
                            new Dictionary<int, Lazy<ClassFactory>>() :
                            new Dictionary<int, Lazy<ClassFactory>>(_compactTypeIdClassFactoryCache);
                    foreach ((int type, Lazy<ClassFactory> factory) in compactIdClassFactories)
                    {
                        compactIdClassFactoryCache[type] = factory;
                    }
                    _compactTypeIdClassFactoryCache = compactIdClassFactoryCache;
                }

                if (typeIdRemoteExceptionFactories != null)
                {
                    Dictionary<string, Lazy<RemoteExceptionFactory>>? typeIdRemoteExceptionFactoryCache =
                        _typeIdRemoteExceptionFactoryCache == null ?
                            new Dictionary<string, Lazy<RemoteExceptionFactory>>() :
                            new Dictionary<string, Lazy<RemoteExceptionFactory>>(_typeIdRemoteExceptionFactoryCache);

                    foreach ((string type, Lazy<RemoteExceptionFactory> factory) in typeIdRemoteExceptionFactories)
                    {
                        typeIdRemoteExceptionFactoryCache[type] = factory;
                    }
                    _typeIdRemoteExceptionFactoryCache = typeIdRemoteExceptionFactoryCache;
                }
            }
        }
    }
}
