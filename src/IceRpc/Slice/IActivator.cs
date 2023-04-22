// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Internal;
using System.Reflection;

namespace IceRpc.Slice;

/// <summary>Provides methods that create class and exception instances from Slice type IDs.</summary>
/// <remarks>When decoding a Slice1-encoded buffer, a Slice decoder uses its activator to create a class or exception
/// instance before decoding the instance's fields.</remarks>
/// <seealso cref="SliceDecoder.DecodeClass{T}" />
/// <seealso cref="SliceDecoder.DecodeNullableClass{T}" />
/// <seealso cref="SliceDecoder.DecodeUserException(string?)" />
public interface IActivator
{
    /// <summary>Gets or creates an activator for the Slice types in the specified assembly and its referenced
    /// assemblies.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>An activator that activates the Slice types defined in <paramref name="assembly" /> provided this
    /// assembly contains generated code (as determined by the presence of the <see cref="SliceAttribute" />
    /// attribute). Types defined in assemblies referenced by <paramref name="assembly" /> are included as well,
    /// recursively. The types defined in the referenced assemblies of an assembly with no generated code are not
    /// considered.</returns>
    public static IActivator FromAssembly(Assembly assembly) => ActivatorFactory.Instance.Get(assembly);

    /// <summary>Gets or creates an activator for the Slice types defined in the specified assemblies and their
    /// referenced assemblies.</summary>
    /// <param name="assemblies">The assemblies.</param>
    /// <returns>An activator that activates the Slice types defined in <paramref name="assemblies" /> and their
    /// referenced assemblies. See <see cref="FromAssembly(Assembly)" />.</returns>
    public static IActivator FromAssemblies(params Assembly[] assemblies) =>
        Internal.Activator.Merge(assemblies.Select(ActivatorFactory.Instance.Get));

    /// <summary>Creates an instance of a Slice class based on a type ID.</summary>
    /// <param name="typeId">The Slice type ID.</param>
    /// <param name="decoder">The decoder.</param>
    /// <returns>A new instance of the class identified by <paramref name="typeId" />.</returns>
    object? CreateClassInstance(string typeId, ref SliceDecoder decoder);

    /// <summary>Creates an instance of a Slice exception based on a type ID.</summary>
    /// <param name="typeId">The Slice type ID.</param>
    /// <param name="decoder">The decoder.</param>
    /// <param name="message">The exception message.</param>
    /// <returns>A new instance of the class identified by <paramref name="typeId" />.</returns>
    object? CreateExceptionInstance(string typeId, ref SliceDecoder decoder, string? message);
}
