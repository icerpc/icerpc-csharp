// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace IceRpc
{
    /// <summary>A class factory implementation that creates instances of types using the
    /// <see cref="ClassAttribute"/> attribute.</summary>
    public class ClassFactory : IClassFactory
    {
        /// <summary>An instance of the class factory that is setup to create instances of types from the IceRPC
        /// and the entry assemblies, for types using <see cref="ClassAttribute"/> attribute. This instance is used as
        /// the local default when the application doesn't configured a class factory.</summary>
        /// <seealso cref="Assembly.GetEntryAssembly"/>
        public static ClassFactory Default { get; } =
            new ClassFactory(Assembly.GetEntryAssembly() is Assembly entryAssembly ?
                new Assembly[] { typeof(RemoteException).Assembly, entryAssembly } :
                new Assembly[] { typeof(RemoteException).Assembly});

        private readonly ImmutableDictionary<int, Lazy<Func<AnyClass>>> _compactTypeIdClassFactoryCache =
            ImmutableDictionary<int, Lazy<Func<AnyClass>>>.Empty;
        private readonly ImmutableDictionary<string, Lazy<Func<AnyClass>>> _typeIdClassFactoryCache =
            ImmutableDictionary<string, Lazy<Func<AnyClass>>>.Empty;
        private readonly ImmutableDictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>> _typeIdExceptionFactoryCache =
            ImmutableDictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>>.Empty;

        /// <summary>Constructs a class factory that can create instances of types in the given
        /// <para>assemblies</para>, for types using the <see cref="ClassAttribute"/> attribute.</summary>
        /// <param name="assemblies">The assemblies containing the types that this factory will create.</param>
        public ClassFactory(IEnumerable<Assembly> assemblies)
        {
            var typeIdClassFactories = new Dictionary<string, Lazy<Func<AnyClass>>>();
            var compactIdClassFactories = new Dictionary<int, Lazy<Func<AnyClass>>>();
            var typeIdExceptionFactories =
                new Dictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>>();

            foreach (Assembly assembly in assemblies)
            {
                IEnumerable<ClassAttribute> attributes =
                    assemblies.SelectMany(assembly => assembly.GetCustomAttributes<ClassAttribute>());
                foreach (ClassAttribute attribute in attributes)
                {
                    if (typeof(AnyClass).IsAssignableFrom(attribute.Type))
                    {
                        var factory = new Lazy<Func<AnyClass>>(() => attribute.ClassFactory);
                        if (attribute.CompactTypeId is int compactTypeId)
                        {
                            compactIdClassFactories[compactTypeId] = factory;
                        }
                        typeIdClassFactories[attribute.TypeId] = factory;
                    }
                    else
                    {
                        Debug.Assert(typeof(RemoteException).IsAssignableFrom(attribute.Type));
                        var factory = new Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>(
                            () => attribute.ExceptionFactory);
                        typeIdExceptionFactories[attribute.TypeId] = factory;
                    }
                }
            }
            _typeIdClassFactoryCache = typeIdClassFactories.ToImmutableDictionary();
            _compactTypeIdClassFactoryCache = compactIdClassFactories.ToImmutableDictionary();
            _typeIdExceptionFactoryCache = typeIdExceptionFactories.ToImmutableDictionary();
        }

        /// <inheritdoc/>
        public AnyClass? CreateClassInstance(string typeId) =>
            _typeIdClassFactoryCache.TryGetValue(typeId, out Lazy<Func<AnyClass>>? factory) ? factory.Value() : null;

        /// <inheritdoc/>
        public AnyClass? CreateClassInstance(int compactId) =>
            _compactTypeIdClassFactoryCache.TryGetValue(
                compactId,
                out Lazy<Func<AnyClass>>? factory) ? factory.Value() : null;

        /// <inheritdoc/>
        public RemoteException? CreateRemoteException(string typeId, string? message, RemoteExceptionOrigin origin) =>
            _typeIdExceptionFactoryCache.TryGetValue(
                typeId,
                out Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>? factory) ?
            factory.Value(message, origin) : null;
    }
}
