// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
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

        private readonly IReadOnlyDictionary<string, Lazy<Func<object>>> _typeIdClassFactoryCache;

        private readonly IReadOnlyDictionary<string, Lazy<Func<string, RemoteExceptionOrigin, Ice20Decoder, RemoteException>>> _typeIdExceptionFactoryCache20;

        /// <summary>Constructs a factory for instances of classes with the <see cref="ClassAttribute"/> attribute
        /// provided in the specified <para>assemblies</para>.The types from IceRpc assembly are always implicitly
        /// added.</summary>
        /// <param name="assemblies">The assemblies containing the types that this factory will create.</param>
        public ClassFactory(IEnumerable<Assembly> assemblies)
        {
            // An enumerable of distinct assemblies that always implicitly includes the IceRpc assembly.
            assemblies = assemblies.Concat(new Assembly[] { typeof(ClassFactory).Assembly }).Distinct();

            var typeIdClassFactoryCache = new Dictionary<string, Lazy<Func<object>>>();
            var typeIdExceptionFactoryCache20 =
                new Dictionary<string, Lazy<Func<string, RemoteExceptionOrigin, Ice20Decoder, RemoteException>>>();

            IEnumerable<ClassAttribute> attributes =
                assemblies.SelectMany(assembly => assembly.GetCustomAttributes<ClassAttribute>());
            foreach (ClassAttribute attribute in attributes)
            {
                var factory = new Lazy<Func<object>>(() => attribute.Factory);
                if (attribute.CompactTypeId is string compactTypeId)
                {
                    typeIdClassFactoryCache.Add(compactTypeId, factory);
                }
                typeIdClassFactoryCache.Add(attribute.TypeId, factory);

                // temporary, need separate attribute
                if (typeof(RemoteException).IsAssignableFrom(attribute.Type))
                {
                    typeIdExceptionFactoryCache20.Add(attribute.TypeId, new(() => attribute.ExceptionFactory20));
                }
            }
            _typeIdClassFactoryCache = typeIdClassFactoryCache;
            _typeIdExceptionFactoryCache20 = typeIdExceptionFactoryCache20;
        }

        object? IClassFactory.Create(string typeId) =>
            _typeIdClassFactoryCache.TryGetValue(typeId, out Lazy<Func<object>>? factory) ? factory.Value() : null;

        RemoteException? IClassFactory.CreateRemoteException(
            string typeId,
            string message,
            RemoteExceptionOrigin origin,
            Ice20Decoder decoder) =>
            _typeIdExceptionFactoryCache20.TryGetValue(
                typeId,
                out Lazy<Func<string, RemoteExceptionOrigin, Ice20Decoder, RemoteException>>? factory) ?
            factory.Value(message, origin, decoder) : null;
    }
}
