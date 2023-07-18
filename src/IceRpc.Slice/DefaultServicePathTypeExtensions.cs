// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using ZeroC.Slice;

namespace IceRpc.Slice;

/// <summary>Provides an extension method for <see cref="Type" /> to get the default service path from a generated
/// interface or proxy type.
/// </summary>
public static class DefaultServicePathTypeExtensions
{
    /// <summary>Computes the default service path for this type using its Slice type ID attribute.</summary>
    /// <param name="type">The service interface or proxy struct generated by the Slice compiler.</param>
    /// <returns>The default service path.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="type" /> is not an interface or a struct, or if
    /// it does not have a <see cref="SliceTypeIdAttribute" /> attribute.</exception>
    public static string GetDefaultServicePath(this Type type)
    {
        if (type.IsInterface || type.IsValueType)
        {
            if (type.GetSliceTypeId() is string typeId)
            {
                Debug.Assert(typeId.StartsWith("::", StringComparison.Ordinal));
                return "/" + typeId[2..].Replace("::", ".", StringComparison.Ordinal);
            }
            else
            {
                throw new ArgumentException(
                    $"The type '{type}' doesn't have a {nameof(SliceTypeIdAttribute)} attribute.",
                    nameof(type));
            }
        }
        else
        {
            throw new ArgumentException($"The type '{type}' is neither an interface nor a struct.", nameof(type));
        }
    }
}
