// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace IceRpc
{
    /// <summary>A class factory implementation that creates instances of types using the
    /// <see cref="ClassAttribute"/> attribute.</summary>
    public class ClassFactory : IClassFactory
    {
        /// <summary>The default class factory instance used when the application doesn't configure one. It looks up
        /// types using the <see cref="ClassAttribute"/> attribute in the IceRpc and entry assemblies.</summary>
        /// <seealso cref="Assembly.GetEntryAssembly"/>
        public static ClassFactory Default { get; } = new ClassFactory(
            Assembly.GetEntryAssembly() is Assembly assembly ? new Assembly[] { assembly } : Array.Empty<Assembly>());
        private readonly IReadOnlyDictionary<string, Lazy<Func<AnyClass>>> _typeIdClassFactoryCache;
        private readonly IReadOnlyDictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>> _typeIdExceptionFactoryCache;

        /// <summary>Constructs a factory for instances of classes with the <see cref="ClassAttribute"/> attribute
        /// provided in the specified <para>assemblies</para>.The types from IceRpc assembly are always implicitly
        /// added.</summary>
        /// <param name="assemblies">The assemblies containing the types that this factory will create.</param>
        public ClassFactory(IEnumerable<Assembly> assemblies)
        {
            // An enumerable of distinct assemblies that always implicitly includes the IceRpc assembly.
            assemblies = assemblies.Concat(new Assembly[] { typeof(ClassFactory).Assembly }).Distinct();

            var typeIdClassFactoryCache = new Dictionary<string, Lazy<Func<AnyClass>>>();
            var typeIdExceptionFactoryCache =
                new Dictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>>();

            IEnumerable<ClassAttribute> attributes =
                assemblies.SelectMany(assembly => assembly.GetCustomAttributes<ClassAttribute>());
            foreach (ClassAttribute attribute in attributes)
            {
                if (typeof(AnyClass).IsAssignableFrom(attribute.Type))
                {
                    var factory = new Lazy<Func<AnyClass>>(() => attribute.ClassFactory);
                    if (attribute.CompactTypeId is string compactTypeId)
                    {
                        typeIdClassFactoryCache.Add(compactTypeId, factory);
                    }
                    typeIdClassFactoryCache.Add(attribute.TypeId, factory);
                }
                else
                {
                    Debug.Assert(typeof(RemoteException).IsAssignableFrom(attribute.Type));
                    typeIdExceptionFactoryCache.Add(attribute.TypeId, new(() => attribute.ExceptionFactory));
                }
            }
            _typeIdClassFactoryCache = typeIdClassFactoryCache;
            _typeIdExceptionFactoryCache = typeIdExceptionFactoryCache;
        }

        /// <inheritdoc/>
        public AnyClass? CreateClassInstance(string typeId) =>
            _typeIdClassFactoryCache.TryGetValue(typeId, out Lazy<Func<AnyClass>>? factory) ? factory.Value() : null;

        /// <inheritdoc/>
        public RemoteException? CreateRemoteException(string typeId, string? message, RemoteExceptionOrigin origin) =>
            _typeIdExceptionFactoryCache.TryGetValue(
                typeId,
                out Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>? factory) ?
            factory.Value(message, origin) : null;
    }
}
