// Copyright (c) ZeroC, Inc.

using ZeroC.Slice;

namespace IceRpc;

/// <summary>Provides extension methods for <see cref="Type" />.</summary>
public static class TypeExtensions
{
    /// <summary>Retrieves the default service path with the attribute <see cref="DefaultServicePathAttribute"/>.
    /// </summary>
    /// <param name="type">The type of an interface.</param>
    /// <returns>The default service path.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="type" /> is not an interface, or if
    /// it does not have a <see cref="DefaultServicePathAttribute" /> attribute.</exception>
    public static string GetDefaultServicePath(this Type type)
    {
        if (type.IsInterface)
        {
            object[] attributes = type.GetCustomAttributes(typeof(DefaultServicePathAttribute), false);
            if (attributes.Length == 1 && attributes[0] is DefaultServicePathAttribute defaultServicePath)
            {
                return defaultServicePath.Value;
            }
            else
            {
                throw new ArgumentException(
                    $"The type '{type}' doesn't have a {nameof(DefaultServicePathAttribute)} attribute.",
                    nameof(type));
            }
        }
        else
        {
            throw new ArgumentException($"The type '{type}' is not an interface.", nameof(type));
        }
    }
}
