// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace IceRpc.Slice.Internal
{
    internal delegate object ActivateObject(ref SliceDecoder decoder);

    /// <summary>The default implementation of <see cref="IActivator"/>, which uses a dictionary.</summary>
    internal class Activator : IActivator
    {
        internal static Activator Empty { get; } =
            new Activator(ImmutableDictionary<string, Lazy<ActivateObject>>.Empty);

        private readonly IReadOnlyDictionary<string, Lazy<ActivateObject>> _dict;

        object? IActivator.CreateInstance(string typeId, ref SliceDecoder decoder) =>
            _dict.TryGetValue(typeId, out Lazy<ActivateObject>? factory) ? factory.Value(ref decoder) : null;

        /// <summary>Merge activators into a single activator; duplicate entries are ignored.</summary>
        internal static Activator Merge(IEnumerable<Activator> activators)
        {
            if (activators.Count() == 1)
            {
                return activators.First();
            }
            else
            {
                var dict = new Dictionary<string, Lazy<ActivateObject>>();

                foreach (Activator activator in activators)
                {
                    foreach ((string typeId, Lazy<ActivateObject> factory) in activator._dict)
                    {
                        dict[typeId] = factory;
                    }
                }
                return dict.Count == 0 ? Empty : new Activator(dict);
            }
        }

        internal Activator(IReadOnlyDictionary<string, Lazy<ActivateObject>> dict) => _dict = dict;
    }

    /// <summary>Creates activators from assemblies by processing types in those assemblies.</summary>
    internal class ActivatorFactory
    {
        internal static ActivatorFactory Instance { get; } = new ActivatorFactory();

        private readonly ConcurrentDictionary<Assembly, Activator> _cache = new();

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
                        var dict = new Dictionary<string, Lazy<ActivateObject>>();

                        foreach (Type type in assembly.GetExportedTypes())
                        {
                            if (type.GetSliceTypeId() is string typeId && !type.IsInterface)
                            {
                                var lazy = new Lazy<ActivateObject>(() => CreateActivateObject(type));

                                dict.Add(typeId, lazy);

                                if (type.GetCompactSliceTypeId() is int compactTypeId)
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

            static ActivateObject CreateActivateObject(Type type)
            {
                ConstructorInfo? constructor = type.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new Type[] { typeof(SliceDecoder).MakeByRefType() },
                    null);

                if (constructor == null)
                {
                    throw new InvalidOperationException($"cannot get Slice decoding constructor for '{type}'");
                }

                ParameterExpression decoderParam =
                    Expression.Parameter(typeof(SliceDecoder).MakeByRefType(), "decoder");

                Expression expression = Expression.New(constructor, decoderParam);
                if (type.IsValueType)
                {
                    // Box the expression.
                    expression = Expression.Convert(expression, typeof(object));
                }
                return Expression.Lambda<ActivateObject>(expression, decoderParam).Compile();
            }
        }

        private ActivatorFactory()
        {
            // ensures it's a singleton.
        }
    }
}
