// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;


namespace IceRpc
{
    class ClassFactory : IClassFactory
    {
        public static ClassFactory Default { get; } = new ClassFactory(GetDefaultAssemblies()); // the local default

        private readonly ImmutableDictionary<string, Lazy<Func<AnyClass>>> _typeIdClassFactoryCache =
            ImmutableDictionary<string, Lazy<Func<AnyClass>>>.Empty;
        private readonly ImmutableDictionary<int, Lazy<Func<AnyClass>>> _compactTypeIdClassFactoryCache =
            ImmutableDictionary<int, Lazy<Func<AnyClass>>>.Empty;
        private readonly ImmutableDictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>> _typeIdExceptionFactoryCache =
            ImmutableDictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>>.Empty;


        public ClassFactory(IEnumerable<Assembly> assemblies)
        {
            var typeIdClassFactories = new Dictionary<string, Lazy<Func<AnyClass>>>();
            var compactIdClassFactories = new Dictionary<int, Lazy<Func<AnyClass>>>();
            var typeIdExceptionFactories = 
                new Dictionary<string, Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>>();

            foreach (Assembly? assembly in assemblies)
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

        public AnyClass? CreateClassInstance(string typeId)
        {
            if (_typeIdClassFactoryCache.TryGetValue(typeId, out Lazy<Func<AnyClass>>? factory))
            {
                return factory.Value();
            }
            else
            {
                return null;
            }
        }
        
        public AnyClass? CreateClassInstance(int compactId)
        {
            if (_compactTypeIdClassFactoryCache.TryGetValue(compactId, out Lazy<Func<AnyClass>>? factory))
            {
                return factory.Value();
            }
            else
            {
                return null;
            }
        }
        
        public RemoteException? CreateRemoteException(string typeId, string? message, RemoteExceptionOrigin origin)
        {
            if (_typeIdExceptionFactoryCache.TryGetValue(
                typeId,
                out Lazy<Func<string?, RemoteExceptionOrigin, RemoteException>>? factory))
            {
                return factory.Value(message, origin);
            }
            else
            {
                return null;
            }
        }

        private static IEnumerable<Assembly> GetDefaultAssemblies()
        {
            var assemblies = new List<Assembly>()
            {
                typeof(ClassFactory).Assembly // The IceRpc assembly
            };
            
            if (Assembly.GetExecutingAssembly() is Assembly executingAssembly)
            {
                assemblies.Add(executingAssembly);
            }
            return assemblies;
        }
    }
}
