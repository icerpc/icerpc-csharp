// Copyright (c) ZeroC, Inc.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace IceRpc.Slice.Internal;

// Instantiates a Slice class or exception. The message is used only for exceptions.
// Note that the decoder is actually not used by the constructor of the Slice class or exception since the actual
// decoding takes place later on.
internal delegate object ActivateObject(ref SliceDecoder decoder, string? message);

/// <summary>The default implementation of <see cref="IActivator" />, which uses a dictionary.</summary>
internal class Activator : IActivator
{
    internal static Activator Empty { get; } =
        new Activator(ImmutableDictionary<string, Lazy<ActivateObject>>.Empty);

    private readonly IReadOnlyDictionary<string, Lazy<ActivateObject>> _dict;

    public object? CreateClassInstance(string typeId, ref SliceDecoder decoder) =>
        _dict.TryGetValue(typeId, out Lazy<ActivateObject>? factory) ? factory.Value(ref decoder, message: null) : null;

    public object? CreateExceptionInstance(string typeId, ref SliceDecoder decoder, string? message) =>
        _dict.TryGetValue(typeId, out Lazy<ActivateObject>? factory) ? factory.Value(ref decoder, message) : null;

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

                    foreach (Type type in assembly.GetTypes())
                    {
                        // We're only interested in generated Slice classes and exceptions.
                        if (type.GetSliceTypeId() is string typeId && type.IsClass)
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
            bool isException = type.IsSubclassOf(typeof(SliceException));

            Type[] types = isException ?
                new Type[] { typeof(SliceDecoder).MakeByRefType(), typeof(string) } :
                new Type[] { typeof(SliceDecoder).MakeByRefType() };

            ConstructorInfo? constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                types,
                null);

            if (constructor is null)
            {
                throw new InvalidOperationException($"Cannot get Slice decoding constructor for '{type}'.");
            }

            ParameterExpression decoderParam =
                Expression.Parameter(typeof(SliceDecoder).MakeByRefType(), "decoder");

            ParameterExpression messageParam = Expression.Parameter(typeof(string), "message");

            Expression expression = isException ?
                Expression.New(constructor, decoderParam, messageParam) :
                Expression.New(constructor, decoderParam);

            return Expression.Lambda<ActivateObject>(expression, decoderParam, messageParam).Compile();
        }
    }

    private ActivatorFactory()
    {
        // ensures it's a singleton.
    }
}
