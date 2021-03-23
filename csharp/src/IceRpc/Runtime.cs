// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
[assembly: InternalsVisibleTo("IceRpc.Tests.Internal")]
[assembly: InternalsVisibleTo("IceRpc.Tests.Encoding")]

namespace IceRpc
{
    /// <summary>The Ice runtime.</summary>
    public static class Runtime
    {
        /// <summary>Returns the Ice version as an integer in the form A.BB.CC, where A indicates the major version,
        /// BB indicates the minor version, and CC indicates the patch level. For example, for Ice 3.3.1, the returned
        /// value is 30301.</summary>
        /// <returns>The Ice version.</returns>
        public const int IntVersion = 40000; // AABBCC, with AA=major, BB=minor, CC=patch

        /// <summary>Returns the Ice version in the form A.B.C, where A indicates the major version, B indicates the
        /// minor version, and C indicates the patch level.</summary>
        /// <returns>The Ice version.</returns>
        public const string StringVersion = "4.0.0-alpha.0"; // "A.B.C", with A=major, B=minor, C=patch

        private static readonly ConcurrentDictionary<string, Func<AnyClass>?> _classFactoryCache = new();
        private static readonly ConcurrentDictionary<int, Func<AnyClass>?> _compactIdCache = new();

        private static HashSet<Assembly> _loadedAssemblies = new();

        private static object _mutex = new();

        private static readonly ConcurrentDictionary<string, Func<string?, RemoteExceptionOrigin, RemoteException>?> _remoteExceptionFactoryCache =
            new();

        /// <summary>Register assembly as an assembly containing class and exception factories. If no assemblies are
        /// registered the Runtime will automatically populate the list of registered assemblies with all referenced
        /// assemblies the first time it tries to unmarshal a class or exception for which no factory can be found.
        /// </summary>
        public static void RegisterApplicationAssembly(Assembly assembly)
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

        /// <summary>Register all referenced assemblies as assemblies containing class and exception factories. if no
        /// assemblies are registered the Runtime will automatically populate the list of registered assemblies with
        /// all referenced assemblies the first time it has to unmarshal a class or exception for which no factory can
        /// be found.</summary>
        public static void RegisterReferencedAssemblies()
        {
            var executingAssembly = Assembly.GetExecutingAssembly();
            var context = AssemblyLoadContext.GetLoadContext(executingAssembly) ?? AssemblyLoadContext.Default;

            var loadedAssemblies = new HashSet<Assembly>();
            foreach (var assembly in context.Assemblies)
            {
                LoadReferencedAssemblies(assembly, loadedAssemblies);
            }
            lock (_mutex)
            {
                _loadedAssemblies = loadedAssemblies;
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

        private static Type? FindType(string csharpId)
        {
            Type? t;

            lock (_mutex)
            {
                if (_loadedAssemblies.Count == 0)
                {
                    RegisterReferencedAssemblies();
                }
            }

            foreach (Assembly a in _loadedAssemblies)
            {
                if ((t = a.GetType(csharpId)) != null)
                {
                    return t;
                }
            }
            return null;
        }

        private static void LoadReferencedAssemblies(Assembly entryAssembly, HashSet<Assembly> seenAssembly)
        {
            lock (_mutex)
            {
                if (seenAssembly.Add(entryAssembly))
                {
                    try
                    {
                        var context = AssemblyLoadContext.GetLoadContext(entryAssembly) ?? AssemblyLoadContext.Default;
                        foreach (AssemblyName name in entryAssembly.GetReferencedAssemblies())
                        {
                            try
                            {
                                var assembly = context.LoadFromAssemblyName(name);
                                LoadReferencedAssemblies(assembly, seenAssembly);
                            }
                            catch
                            {
                                // Ignore assemblies that cannot be loaded.
                            }
                        }
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // Some platforms like UWP do not support using GetReferencedAssemblies
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
