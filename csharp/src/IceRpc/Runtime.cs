// Copyright (c) ZeroC, Inc. All rights reserved.

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
[assembly: InternalsVisibleTo("IceRpc.Tests.CodeGeneration")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Internal")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Encoding")]

namespace IceRpc
{
    /// <summary>The Ice runtime.</summary>
    public static class Runtime
    {
        /// <summary>The IceRPC version in semver format.</summary>
        public const string StringVersion = "0.0.1-alpha";

        /// <summary>Gets or sets the logger factory used by IceRpc classes when no logger factory is explicitely
        /// configured.</summary>
        public static ILoggerFactory DefaultLoggerFactory { get; set; } = NullLoggerFactory.Instance;

        private static readonly ConcurrentDictionary<string, Func<AnyClass>?> _classFactoryCache = new();
        private static readonly ConcurrentDictionary<int, Func<AnyClass>?> _compactIdCache = new();

        private static HashSet<Assembly> _loadedAssemblies = new();

        // The mutex protects _loadedAssemblies
        private static readonly object _mutex = new();

        private static readonly ConcurrentDictionary<string, Func<string?, RemoteExceptionOrigin, RemoteException>?> _remoteExceptionFactoryCache =
            new();

        private static readonly IDictionary<string, (Ice1EndpointParser? Ice1Parser, Ice2EndpointParser? Ice2Parser, Transport Transport)> _transportNameRegistry =
            new ConcurrentDictionary<string, (Ice1EndpointParser?, Ice2EndpointParser?, Transport)>();

        private static readonly IDictionary<Transport, (EndpointFactory Factory, Ice1EndpointFactory? Ice1Factory, Ice1EndpointParser? Ice1Parser, Ice2EndpointParser? Ice2Parser)> _transportRegistry =
            new ConcurrentDictionary<Transport, (EndpointFactory, Ice1EndpointFactory?, Ice1EndpointParser?, Ice2EndpointParser?)>();

        /// <summary>Register an assembly containing class or exception factories. If no assemblies are
        /// registered explicitly with this method, all the assemblies referenced from the executing assembly
        /// will be registered the first time IceRPC looks up a factory.</summary>
        public static void RegisterFactoriesFromAssembly(Assembly assembly)
        {
            HashSet<Assembly> loadedAssemblies;
            lock (_mutex)
            {
                loadedAssemblies = new HashSet<Assembly>(_loadedAssemblies);
            }
            LoadReferencedAssemblies(assembly, loadedAssemblies);
            lock (_mutex)
            {
                _loadedAssemblies = loadedAssemblies;
            }
        }

        /// <summary>Registers all the assemblies referenced by the executing assembly. If this method is not called
        /// an no application assemblies are registered through <see cref="RegisterFactoriesFromAssembly"/>, this method
        /// is called implicitly when IceRPC looks up a class or exception factory.</summary>
        public static void RegisterFactoriesFromAllAssemblies()
        {
            var loadedAssemblies = new HashSet<Assembly>();
            foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
            {
                LoadReferencedAssemblies(assembly, loadedAssemblies);
            }
            lock (_mutex)
            {
                _loadedAssemblies = loadedAssemblies;
            }
        }

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
                UriParser.RegisterTransport(transportName, defaultUriPort);
            }
        }

        static Runtime()
        {
            // Register the ice and ice+universal schemes with the system UriParser.
            UriParser.RegisterTransport("universal", UniversalEndpoint.DefaultUniversalPort);
            UriParser.RegisterIceScheme();

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

        // Returns the IClassFactory associated with this Slice type ID, not null if not found.
        internal static Func<AnyClass>? FindClassFactory(string typeId) =>
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

        internal static Func<AnyClass>? FindClassFactory(int compactId) =>
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

        internal static Func<string?, RemoteExceptionOrigin, RemoteException>? FindRemoteExceptionFactory(string typeId) =>
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

        private static Type? FindType(string className)
        {
            lock (_mutex)
            {
                if (_loadedAssemblies.Count == 0)
                {
                    // Lazzy initialization
                    RegisterFactoriesFromAllAssemblies();
                }
            }
            return _loadedAssemblies.Select(
                assembly => assembly.GetType(className)).FirstOrDefault(type => type != null);
        }

        private static void LoadReferencedAssemblies(Assembly entryAssembly, HashSet<Assembly> seenAssembly)
        {
            if (seenAssembly.Add(entryAssembly))
            {
                foreach (AssemblyName name in entryAssembly.GetReferencedAssemblies())
                {
                    try
                    {
                        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(name);
                        LoadReferencedAssemblies(assembly, seenAssembly);
                    }
                    catch
                    {
                        // Ignore assemblies that cannot be loaded.
                    }
                }
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
