// Copyright (c) ZeroC, Inc.

// TODO: temporary, for paramref. See #4220.
#pragma warning disable CS1734 // XML comment has a type parameter reference that is not valid.

namespace IceRpc;

/// <summary>Provides extension methods for <see cref="Type" />.</summary>
public static class TypeExtensions
{
    /// <summary>Extension methods for <see cref="Type" />.</summary>
    /// <param name="type">The type of an interface.</param>
    extension(Type type)
    {
        /// <summary>Retrieves the default service path with the attribute <see cref="DefaultServicePathAttribute"/>.
        /// </summary>
        /// <returns>The default service path.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="type" /> is not an interface, or if
        /// it does not have a <see cref="DefaultServicePathAttribute" /> attribute.</exception>
        public string GetDefaultServicePath()
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
                        $"Interface '{type}' does not have a {nameof(DefaultServicePathAttribute)} attribute.",
                        nameof(type));
                }
            }
            else
            {
                throw new ArgumentException($"The type '{type}' is not an interface.", nameof(type));
            }
        }
    }
}
