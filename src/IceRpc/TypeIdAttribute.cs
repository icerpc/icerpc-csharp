// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>This attribute class is used by the generated code to assign a type ID to C# classes, interfaces and
    /// structs mapped from Slice interfaces, classes and exceptions. </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, Inherited = false)]
    public sealed class TypeIdAttribute : Attribute
    {
        /// <summary>Retrieves the type ID.</summary>
        /// <value>The type ID string.</value>
        public string Value { get; }

        /// <summary>Constructs a TypeIdAttribute.</summary>
        /// <param name="value">The type ID.</param>>
        public TypeIdAttribute(string value) => Value = value;
    }

    /// <summary>This attribute class is used by the generated code to assign a compact type ID to C# classes mapped
    /// from Slice classes. </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CompactTypeIdAttribute : Attribute
    {
        /// <summary>Retrieves the compact type ID.</summary>
        /// <value>The compact type ID numeric value.</value>
        public int Value { get; }

        /// <summary>Constructs a CompactTypeIdAttribute.</summary>
        /// <param name="value">The compact type ID.</param>>
        public CompactTypeIdAttribute(int value) => Value = value;
    }

    /// <summary>This class adds extension methods to System.Type.</summary>
    public static class TypeExtensions
    {
        /// <summary>Retrieves the Ice type ID from a type with the attribute IceRpc.TypeId.</summary>
        /// <param name="type">The class, interface or struct generated by the Slice compiler.</param>
        /// <returns>The type ID, or null if type does not carry the IceRpc.TypeId attribute.</returns>
        public static string? GetIceTypeId(this Type type)
        {
            object[] attributes = type.GetCustomAttributes(typeof(TypeIdAttribute), false);
            return attributes.Length == 1 && attributes[0] is TypeIdAttribute typeId ? typeId.Value : null;
        }

        /// <summary>Retrieves the Ice compact type ID from a type with the attribute IceRpc.CompactTypeId.</summary>
        /// <param name="type">The class or interface generated by the Slice compiler.</param>
        /// <returns>The compact type ID, or null if type does not carry the IceRpc.CompactTypeId attribute.</returns>
        public static int? GetIceCompactTypeId(this Type type)
        {
            object[] attributes = type.GetCustomAttributes(typeof(CompactTypeIdAttribute), false);
            return attributes.Length == 1 && attributes[0] is CompactTypeIdAttribute typeId ? typeId.Value : null;
        }

        /// <summary>Retrieves the Ice type ID from a type and from all its base types.
        /// When type is an interface, it returns the type ID for that interface, plus all its base interfaces, and
        /// these type IDs are returned in alphabetical order.
        /// When type is a class, it returns the type ID of that class plus the type ID of the base classes. These
        /// type IDs are sorted from most derived to least derived.</summary>
        /// <param name="type">The class or interface generated by the Slice compiler.</param>
        /// <returns>An array of Ice type IDs.</returns>
        public static string[] GetAllIceTypeIds(this Type type)
        {
            if (type.IsInterface)
            {
                var result = new List<string>();
                if (GetIceTypeId(type) is string firstTypeId)
                {
                    result.Add(firstTypeId);
                    Type[] interfaces = type.GetInterfaces();
                    foreach (Type p in interfaces)
                    {
                        if (GetIceTypeId(p) is string typeId)
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
                for (Type? p = type; p != null; p = p.BaseType)
                {
                    if (GetIceTypeId(p) is string typeId)
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
            else if (GetIceTypeId(type) is string typeId)
            {
                return new string[] { typeId };
            }
            else
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Computes the default path for this type using its Ice type ID attribute.</summary>
        /// <param name="type">The interface generated by the Slice compiler.</param>
        /// <returns>The default path.</returns>
        public static string GetDefaultPath(this Type type)
        {
            if (type.GetIceTypeId() is string typeId)
            {
                Debug.Assert(typeId.StartsWith("::", StringComparison.Ordinal));
                return "/" + typeId[2..].Replace("::", ".", StringComparison.Ordinal);
            }
            else
            {
                throw new ArgumentException($"{type.FullName} doesn't have an IceRpc.TypeId attribute",
                                            nameof(type));
            }
        }
    }
}
