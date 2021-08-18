// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Reflection;

namespace IceRpc
{
    /// <summary>A class factory implementation that creates instances of types using the
    /// <see cref="RemoteExceptionAttribute"/> attribute.</summary>
    public class RemoteExceptionFactory : IRemoteExceptionFactory
    {
        /// <summary>The default class factory instance used when the application doesn't configure one. It looks up
        /// types using the <see cref="RemoteExceptionAttribute"/> attribute in the IceRpc and entry assemblies.
        /// </summary>
        /// <seealso cref="Assembly.GetEntryAssembly"/>
        public static RemoteExceptionFactory Default { get; } = new RemoteExceptionFactory(
            Assembly.GetEntryAssembly() is Assembly assembly ? new Assembly[] { assembly } : Array.Empty<Assembly>());

        private readonly IReadOnlyDictionary<string, Lazy<Func<string, RemoteExceptionOrigin, Ice20Decoder, RemoteException>>> _factoryCache;

        /// <summary>Constructs a factory for instances of classes with the <see cref="RemoteExceptionAttribute"/>
        /// attribute provided in the specified <para>assemblies</para>.The types from IceRpc assembly are always
        /// implicitly added.</summary>
        /// <param name="assemblies">The assemblies containing the types that this factory will create.</param>
        public RemoteExceptionFactory(IEnumerable<Assembly> assemblies)
        {
            // An enumerable of distinct assemblies that always implicitly includes the IceRpc assembly.
            assemblies = assemblies.Concat(new Assembly[] { typeof(RemoteExceptionFactory).Assembly }).Distinct();

            var factoryCache =
                new Dictionary<string, Lazy<Func<string, RemoteExceptionOrigin, Ice20Decoder, RemoteException>>>();

            IEnumerable<RemoteExceptionAttribute> attributes =
                assemblies.SelectMany(assembly => assembly.GetCustomAttributes<RemoteExceptionAttribute>());
            foreach (RemoteExceptionAttribute attribute in attributes)
            {
                factoryCache.Add(attribute.TypeId, new(() => attribute.Factory));
            }
            _factoryCache = factoryCache;
        }

        RemoteException? IRemoteExceptionFactory.CreateRemoteException(
            string typeId,
            string message,
            RemoteExceptionOrigin origin,
            Ice20Decoder decoder) =>
            _factoryCache.TryGetValue(
                typeId,
                out Lazy<Func<string, RemoteExceptionOrigin, Ice20Decoder, RemoteException>>? factory) ?
            factory.Value(message, origin, decoder) : null;
    }
}
