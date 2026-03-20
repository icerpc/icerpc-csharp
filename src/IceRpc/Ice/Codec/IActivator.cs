// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec.Internal;
using System.Reflection;

namespace IceRpc.Ice.Codec;

/// <summary>Provides methods that create class and exception instances from Ice type IDs.</summary>
/// <remarks>When decoding a buffer, an Ice decoder uses its activator to create a class or exception instance before
/// decoding the instance's fields.</remarks>
/// <seealso cref="IceDecoder" />
public interface IActivator
{
    /// <summary>Gets or creates an activator for the Ice types in the specified assembly and its referenced
    /// assemblies.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>An activator that activates the Ice types defined in <paramref name="assembly" /> provided this
    /// assembly contains generated code (as determined by the presence of the <see cref="IceGeneratedCodeAttribute" /> attribute).
    /// The Ice types defined in assemblies referenced by <paramref name="assembly" /> are included as well,
    /// recursively. If a referenced assembly contains no generated code, the assemblies it references are not examined.
    /// </returns>
    public static IActivator FromAssembly(Assembly assembly) => ActivatorFactory.Instance.Get(assembly);

    /// <summary>Gets or creates an activator for the Ice types defined in the specified assemblies and their
    /// referenced assemblies.</summary>
    /// <param name="assemblies">The assemblies.</param>
    /// <returns>An activator that activates the Ice types defined in <paramref name="assemblies" /> and their
    /// referenced assemblies. See <see cref="FromAssembly(Assembly)" />.</returns>
    public static IActivator FromAssemblies(params Assembly[] assemblies) =>
        Internal.Activator.Merge(assemblies.Select(ActivatorFactory.Instance.Get));

    /// <summary>Creates an instance of an Ice class or Ice exception based on a type ID.</summary>
    /// <param name="typeId">The Ice type ID.</param>
    /// <returns>A new instance of the class identified by <paramref name="typeId" />, or null if the implementation
    /// cannot find the corresponding C# class.</returns>
    /// <remarks>This implementation of this method can also throw an exception if the class is found but the activation
    /// of an instance fails.</remarks>
    object? CreateInstance(string typeId);
}
