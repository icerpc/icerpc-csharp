// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Slice;

/// <summary>This class adds extension methods to System.Type.</summary>
public static class TypeExtensions
{
    /// <summary>Retrieves the Slice type ID from a type with the attribute IceRpc.TypeId.</summary>
    /// <param name="type">The type of a class or interface generated by the Slice compiler.</param>
    /// <returns>The Slice type ID, or null if <paramref name="type" /> does not carry the IceRpc.TypeId attribute.
    /// </returns>
    public static string? GetSliceTypeId(this Type type)
    {
        object[] attributes = type.GetCustomAttributes(typeof(TypeIdAttribute), false);
        return attributes.Length == 1 && attributes[0] is TypeIdAttribute typeId ? typeId.Value : null;
    }

    /// <summary>Retrieves the compact Slice type ID from a type with the attribute IceRpc.CompactTypeId.</summary>
    /// <param name="type">The type of a class generated by the Slice compiler.</param>
    /// <returns>The compact Slice type ID, or null if <paramref name="type" /> does not carry the IceRpc.CompactTypeId
    /// attribute.</returns>
    public static int? GetCompactSliceTypeId(this Type type)
    {
        object[] attributes = type.GetCustomAttributes(typeof(CompactTypeIdAttribute), false);
        return attributes.Length == 1 && attributes[0] is CompactTypeIdAttribute typeId ? typeId.Value : null;
    }

    /// <summary>Retrieves the Slice type ID from a type and from all its base types.
    /// When type is an interface, it returns the type ID for that interface, plus all its base interfaces, and
    /// these type IDs are returned in alphabetical order.
    /// When type is a class, it returns the type ID of that class plus the type ID of the base classes. These
    /// type IDs are sorted from most derived to least derived.</summary>
    /// <param name="type">The type of a class or interface generated by the Slice compiler.</param>
    /// <returns>An array of Slice type IDs.</returns>
    public static string[] GetAllSliceTypeIds(this Type type)
    {
        if (type.IsInterface)
        {
            var result = new List<string>();
            if (GetSliceTypeId(type) is string firstTypeId)
            {
                result.Add(firstTypeId);
                Type[] interfaces = type.GetInterfaces();
                foreach (Type p in interfaces)
                {
                    if (GetSliceTypeId(p) is string typeId)
                    {
                        result.Add(typeId);
                    }
                }
                result.Sort(StringComparer.Ordinal);
            }
            return result.ToArray();
        }
        else if (type.IsClass)
        {
            var result = new List<string>();
            for (Type? p = type; p is not null; p = p.BaseType)
            {
                if (GetSliceTypeId(p) is string typeId)
                {
                    result.Add(typeId);
                }
                else
                {
                    break; // for
                }
            }
            return result.ToArray();
        }
        else if (GetSliceTypeId(type) is string typeId)
        {
            return new string[] { typeId };
        }
        else
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Computes the default path for this type using its Slice type ID attribute.</summary>
    /// <param name="type">The interface generated by the Slice compiler.</param>
    /// <returns>The default path.</returns>
    public static string GetDefaultPath(this Type type)
    {
        if (type.GetSliceTypeId() is string typeId)
        {
            Debug.Assert(typeId.StartsWith("::", StringComparison.Ordinal));
            return "/" + typeId[2..].Replace("::", ".", StringComparison.Ordinal);
        }
        else
        {
            throw new ArgumentException($"{type} doesn't have an IceRpc.TypeId attribute", nameof(type));
        }
    }
}
