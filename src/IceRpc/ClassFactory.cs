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
        public static ClassFactory Default { get; } = new ClassFactory(
            Assembly.GetEntryAssembly() is Assembly assembly ? new Assembly[] { assembly } : Array.Empty<Assembly>());

        private readonly ImmutableDictionary<int, Lazy<Func<AnyClass>>> _compactTypeIdClassFactoryCache =
            ImmutableDictionary<int, Lazy<Func<AnyClass>>>.Empty;
        private readonly ImmutableDictionary<string, Lazy<Func<AnyClass>>> _typeIdClassFactoryCache =
            ImmutableDictionary<string, Lazy<Func<AnyClass>>>.Empty;
        private readonly ImmutableDictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>> _typeIdExceptionFactoryCache =
            ImmutableDictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>>.Empty;

        /// <summary>Constructs a factory for instances of classes with the <see cref="ClassAttribute"/> attribute
        /// provided in the specified <para>assemblies</para>.</summary>
        /// <param name="assemblies">The assemblies containing the types that this factory will create.</param>
        public ClassFactory(IEnumerable<Assembly> assemblies)
        {
            ImmutableDictionary<string, Lazy<Func<AnyClass>>>.Builder typeIdClassFactoriesBuilder =
                ImmutableDictionary.CreateBuilder<string, Lazy<Func<AnyClass>>>();
            ImmutableDictionary<int, Lazy<Func<AnyClass>>>.Builder compactIdClassFactoriesBuilder =
                ImmutableDictionary.CreateBuilder<int, Lazy<Func<AnyClass>>>();
            ImmutableDictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>>.Builder typeIdExceptionFactoriesBuilder =
                ImmutableDictionary.CreateBuilder<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>>();

            // An enumerable of distinct assemblies that always implicitly includes IceRpc assembly
            assemblies = assemblies.Concat(new Assembly[] { typeof(ClassFactory).Assembly }).Distinct();

            IEnumerable<ClassAttribute> attributes =
                assemblies.SelectMany(assembly => assembly.GetCustomAttributes<ClassAttribute>());
            foreach (ClassAttribute attribute in attributes)
            {
                if (typeof(AnyClass).IsAssignableFrom(attribute.Type))
                {
                    var factory = new Lazy<Func<AnyClass>>(() => attribute.ClassFactory);
                    if (attribute.CompactTypeId is int compactTypeId)
                    {
                        compactIdClassFactoriesBuilder.Add(compactTypeId, factory);
                    }
                    typeIdClassFactoriesBuilder.Add(attribute.TypeId, factory);
                }
                else
                {
                    Debug.Assert(typeof(RemoteException).IsAssignableFrom(attribute.Type));
                    var factory = new Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>(
                        () => attribute.ExceptionFactory);
                    typeIdExceptionFactoriesBuilder.Add(attribute.TypeId, factory);
                }
            }

            _typeIdClassFactoryCache = typeIdClassFactoriesBuilder.ToImmutableDictionary();
            _compactTypeIdClassFactoryCache = compactIdClassFactoriesBuilder.ToImmutableDictionary();
            _typeIdExceptionFactoryCache = typeIdExceptionFactoriesBuilder.ToImmutableDictionary();
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
