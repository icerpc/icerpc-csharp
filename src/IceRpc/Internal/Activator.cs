// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace IceRpc.Internal
{
    /// <summary>The default implementation of <see cref="IActivator{T}"/>, which uses a dictionary.</summary>
    internal class Activator<T> : IActivator<T> where T : IceDecoder
    {
        internal static Activator<T> Empty { get; } =
            new Activator<T>(ImmutableDictionary<string, Lazy<Func<T, object>>>.Empty);

        private IReadOnlyDictionary<string, Lazy<Func<T, object>>> _dict;

        object? IActivator<T>.CreateInstance(string typeId, T decoder) =>
            _dict.TryGetValue(typeId, out Lazy<Func<T, object>>? factory) ? factory.Value(decoder) : null;

        /// <summary>Merge activators into a single activator; duplicate entries are ignored.</summary>
        internal static Activator<T> Merge(IEnumerable<Activator<T>> activators)
        {
            if (activators.Count() == 1)
            {
                return activators.First();
            }
            else
            {
                var dict = new Dictionary<string, Lazy<Func<T, object>>>();

                foreach (Activator<T> activator in activators)
                {
                    foreach ((string typeId, Lazy<Func<T, object>> factory) in activator._dict)
                    {
                        dict[typeId] = factory;
                    }
                }
                return dict.Count == 0 ? Empty : new Activator<T>(dict);
            }
        }

        internal Activator(IReadOnlyDictionary<string, Lazy<Func<T, object>>> dict) => _dict = dict;
    }

    /// <summary>Creates activators from assemblies by processing types in those assemblies.</summary>
    internal class ActivatorFactory<T> where T : IceDecoder
    {
        private readonly ConcurrentDictionary<Assembly, Activator<T>> _cache = new();

        private readonly Func<Type, bool> _typeFilter;

        internal ActivatorFactory(Func<Type, bool> typeFilter) => _typeFilter = typeFilter;

        internal Activator<T> Get(Assembly assembly)
        {
            if (_cache.TryGetValue(assembly, out Activator<T>? activator))
            {
                return activator;
            }
            else if (assembly.GetCustomAttributes<SliceAttribute>().Any())
            {
                return _cache.GetOrAdd(
                    assembly,
                    assembly =>
                    {
                        var dict = new Dictionary<string, Lazy<Func<T, object>>>();

                        foreach (Type type in assembly.GetExportedTypes())
                        {
                            if (type.GetIceTypeId() is string typeId && _typeFilter(type))
                            {
                                dict.Add(typeId, new Lazy<Func<T, object>>(CreateFactory(type)));
                            }
                        }

                        // Merge with the activators of the referenced assemblies (recursive call)
                        return Activator<T>.Merge(
                            assembly.GetReferencedAssemblies().Select(
                                assemblyName => Get(AppDomain.CurrentDomain.Load(assemblyName))).Append(
                                    new Activator<T>(dict)));
                    });
            }
            else
            {
                // We don't cache an assembly with no Slice attribute, and don't load/process its referenced assemblies.
                return Activator<T>.Empty;
            }

            static Func<T, object> CreateFactory(Type type)
            {
                ConstructorInfo? constructor = type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new Type[] { typeof(T) },
                    null);

                    if (constructor == null)
                    {
                        throw new InvalidOperationException(
                            $"cannot get Ice decoding constructor for '{type.FullName}'");
                    }

                    ParameterExpression decoderParam = Expression.Parameter(typeof(T), "decoder");

                    return Expression.Lambda<Func<T, object>>(
                        Expression.New(constructor, decoderParam), decoderParam).Compile();
            }
        }
    }
}
