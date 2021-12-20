// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace IceRpc.Slice.Internal
{
    /// <summary>The default implementation of <see cref="IActivator"/>, which uses a dictionary.</summary>
    internal class Activator : IActivator
    {
        internal static Activator Empty { get; } =
            new Activator(ImmutableDictionary<string, Lazy<Func<IceDecoder, object>>>.Empty);

        private readonly IReadOnlyDictionary<string, Lazy<Func<IceDecoder, object>>> _dict;

        object? IActivator.CreateInstance(string typeId, IceDecoder decoder) =>
            _dict.TryGetValue(typeId, out Lazy<Func<IceDecoder, object>>? factory) ? factory.Value(decoder) : null;

        /// <summary>Merge activators into a single activator; duplicate entries are ignored.</summary>
        internal static Activator Merge(IEnumerable<Activator> activators)
        {
            if (activators.Count() == 1)
            {
                return activators.First();
            }
            else
            {
                var dict = new Dictionary<string, Lazy<Func<IceDecoder, object>>>();

                foreach (Activator activator in activators)
                {
                    foreach ((string typeId, Lazy<Func<IceDecoder, object>> factory) in activator._dict)
                    {
                        dict[typeId] = factory;
                    }
                }
                return dict.Count == 0 ? Empty : new Activator(dict);
            }
        }

        internal Activator(IReadOnlyDictionary<string, Lazy<Func<IceDecoder, object>>> dict) => _dict = dict;
    }

    /// <summary>Creates activators from assemblies by processing types in those assemblies.</summary>
    internal class ActivatorFactory
    {
        private readonly ConcurrentDictionary<Assembly, Activator> _cache = new();

        private readonly Func<Type, bool> _typeFilter;

        internal ActivatorFactory(Func<Type, bool> typeFilter) => _typeFilter = typeFilter;

        internal Activator Get(Assembly assembly)
        {
            if (_cache.TryGetValue(assembly, out Activator? activator))
            {
                return activator;
            }
            else if (assembly.GetCustomAttributes<SliceAttribute>().Any())
            {
                return _cache.GetOrAdd(
                    assembly,
                    assembly =>
                    {
                        var dict = new Dictionary<string, Lazy<Func<IceDecoder, object>>>();

                        foreach (Type type in assembly.GetExportedTypes())
                        {
                            if (type.GetIceTypeId() is string typeId && _typeFilter(type))
                            {
                                var lazy = new Lazy<Func<IceDecoder, object>>(() => CreateFactory(type));

                                dict.Add(typeId, lazy);

                                if (type.GetIceCompactTypeId() is int compactTypeId)
                                {
                                    dict.Add(compactTypeId.ToString(CultureInfo.InvariantCulture), lazy);
                                }
                            }
                        }

                        // Merge with the activators of the referenced assemblies (recursive call)
                        return Activator.Merge(
                            assembly.GetReferencedAssemblies().Select(
                                assemblyName => Get(AppDomain.CurrentDomain.Load(assemblyName))).Append(
                                    new Activator(dict)));
                    });
            }
            else
            {
                // We don't cache an assembly with no Slice attribute, and don't load/process its referenced assemblies.
                return Activator.Empty;
            }

            static Func<IceDecoder, object> CreateFactory(Type type)
            {
                ConstructorInfo? constructor = type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new Type[] { typeof(IceDecoder) },
                    null);

                if (constructor == null)
                {
                    throw new InvalidOperationException($"cannot get Ice decoding constructor for '{type}'");
                }

                ParameterExpression decoderParam = Expression.Parameter(typeof(IceDecoder), "decoder");

                return Expression.Lambda<Func<IceDecoder, object>>(
                    Expression.New(constructor, decoderParam), decoderParam).Compile();
            }
        }
    }
}
