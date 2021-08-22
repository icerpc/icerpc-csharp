﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Reflection;

namespace IceRpc
{
    /// <summary>A class factory implementation that creates instances of types using the <see cref="ClassAttribute"/>
    /// attribute.</summary>
    public class ClassFactory : IActivator<Ice11Decoder>
    {
        /// <summary>The default class factory instance used when the application doesn't configure one. It looks up
        /// types using the <see cref="ClassAttribute"/> attribute in the IceRpc and entry assemblies.</summary>
        /// <seealso cref="Assembly.GetEntryAssembly"/>
        public static ClassFactory Default { get; } = new ClassFactory(
            Assembly.GetEntryAssembly() is Assembly assembly ? new Assembly[] { assembly } : Array.Empty<Assembly>());

        private readonly IReadOnlyDictionary<string, Lazy<Func<Ice11Decoder, object>>> _factoryCache;

        /// <summary>Constructs a class factory for instances of classes with the <see cref="ClassAttribute"/> attribute
        /// provided in the specified <para>assemblies</para>.The types from IceRpc assembly are always implicitly
        /// added.</summary>
        /// <param name="assemblies">The assemblies containing the types that this factory will create.</param>
        public ClassFactory(IEnumerable<Assembly> assemblies)
        {
            // An enumerable of distinct assemblies that always implicitly includes the IceRpc assembly.
            assemblies = assemblies.Concat(new Assembly[] { typeof(ClassFactory).Assembly }).Distinct();

            var factoryCache = new Dictionary<string, Lazy<Func<Ice11Decoder, object>>>();

            IEnumerable<ClassAttribute> attributes =
                assemblies.SelectMany(assembly => assembly.GetCustomAttributes<ClassAttribute>());

            foreach (ClassAttribute attribute in attributes)
            {
                var factory = new Lazy<Func<Ice11Decoder, object>>(() => attribute.Factory);
                factoryCache.Add(attribute.TypeId, factory);
                if (attribute.CompactTypeId is string compactTypeId)
                {
                    factoryCache.Add(compactTypeId, factory);
                }
            }

            // Add factory for plain RemoteException
            factoryCache.Add(typeof(RemoteException).GetIceTypeId()!,
                             new Lazy<Func<Ice11Decoder, object>>(decoder => new RemoteException(decoder)));

            _factoryCache = factoryCache;
        }

        object? IActivator<Ice11Decoder>.CreateInstance(string typeId, Ice11Decoder decoder) =>
            _factoryCache.TryGetValue(typeId, out Lazy<Func<Ice11Decoder, object>>? factory) ?
                factory.Value(decoder) : null;
    }
}
