// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.Reflection;

namespace IceRpc;

/// <summary>Provides extension methods for <see cref="Type" />.</summary>
public static class TypeExtensions
{
    /// <summary>Retrieves the default service path, as set by the attribute <see cref="DefaultServicePathAttribute"/>.
    /// </summary>
    /// <param name="type">The interface with the <see cref="DefaultServicePathAttribute"/> attribute, or a class
    /// that implements such an interface.</param>
    /// <returns>The default service path.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="type" /> is neither a class nor an interface, or
    /// if it does not have a <see cref="DefaultServicePathAttribute" /> attribute.</exception>
    /// <exception cref="AmbiguousMatchException">Thrown if <paramref name="type" /> is a class that implements multiple
    /// interfaces with a <see cref="DefaultServicePathAttribute" /> attribute.</exception>
    /// <remarks>When <paramref name="type" /> is an interface, this method only searches for the attribute on the
    /// interface itself.</remarks>
    /// <seealso cref="RouterExtensions.Map{TService}(Router, IDispatcher)"/>
    /// <seealso cref="RouterExtensions.Map{TService}(Router, TService)"/>
    public static string GetDefaultServicePath(this Type type)
    {
        if (type.IsClass)
        {
            string? defaultServicePath = null;

            foreach (Type interfaceType in type.GetInterfaces())
            {
                if (GetDefaultServicePathForInterface(interfaceType) is string path)
                {
                    if (defaultServicePath is null)
                    {
                        defaultServicePath = path;
                    }
                    else
                    {
                        // GetInterfaces() does not return the same interface more than once.
                        throw new AmbiguousMatchException(
                            $"The class '{type}' implements multiple interfaces with a {nameof(DefaultServicePathAttribute)} attribute.");
                    }
                }
            }

            return defaultServicePath ?? throw new ArgumentException(
                    $"The class '{type}' does not implement any interface with a {nameof(DefaultServicePathAttribute)} attribute.",
                    nameof(type));
        }
        else if (type.IsInterface)
        {
            return GetDefaultServicePathForInterface(type) ?? throw new ArgumentException(
                $"The interface '{type}' does not have a {nameof(DefaultServicePathAttribute)} attribute.",
                nameof(type));
        }
        else
        {
            throw new ArgumentException($"The type '{type}' is neither a class nor an interface.", nameof(type));
        }

        static string? GetDefaultServicePathForInterface(Type type)
        {
            Debug.Assert(type.IsInterface);
            object[] attributes = type.GetCustomAttributes(typeof(DefaultServicePathAttribute), inherit: false);
            if (attributes.Length == 1)
            {
                return ((DefaultServicePathAttribute)attributes[0]).Value;
            }
            else if (attributes.Length > 1)
            {
                throw new AmbiguousMatchException(
                    $"The interface '{type}' has multiple {nameof(DefaultServicePathAttribute)} attributes.");
            }
            else
            {
                return null;
            }
        }
    }
}
