// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
[assembly: InternalsVisibleTo("IceRpc.Tests.Internal")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Encoding")]

namespace IceRpc
{
    /// <summary>Provides global configuration for IceRPC in the current process.</summary>
    public static class Runtime
    {
        /// <summary>The IceRPC version in semver format.</summary>
        public const string StringVersion = "0.0.1-alpha";

        /// <summary>The timeout for invocations that do not specify a timeout or deadline. The default value is 60s.
        /// </summary>
        /// <seealso cref="Invocation"/>
        public static TimeSpan DefaultInvocationTimeout
        {
            get => _defaultInvocationTimeout;
            set => _defaultInvocationTimeout = value > TimeSpan.Zero || value == Timeout.InfiniteTimeSpan ? value :
                throw new ArgumentException($"{nameof(DefaultInvocationTimeout)} must be greater than 0",
                                            nameof(DefaultInvocationTimeout));
        }

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

        private static TimeSpan _defaultInvocationTimeout = TimeSpan.FromSeconds(60);

        // The mutex protects assignment to class and exception factory caches
        private static readonly object _mutex = new();
        private static IReadOnlyDictionary<string, Lazy<ClassFactory>>? _typeIdClassFactoryCache;
        private static IReadOnlyDictionary<string, Lazy<RemoteExceptionFactory>>? _typeIdRemoteExceptionFactoryCache;

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

        static Runtime()
        {
            // Register the ice and ice+universal schemes with the system UriParser.
            Internal.UriParser.RegisterTransport("universal", defaultPort: 0);
            Internal.UriParser.RegisterIceScheme();
            TransportRegistry.Add(LocEndpoint.LocTransportDescriptor);
        }

        // Must be called before parsing a Uri to make sure the static constructors of Runtime and TransportRegistry
        // executed and registered the URI schemes for the built-in transports.
        internal static void UriInitialize() => TransportRegistry.UriInitialize();

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
