// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice;

/// <summary>Provides extension methods for <see cref="Type" />.</summary>
public static class TypeExtensions
{
    /// <summary>Extension methods for <see cref="Type" />.</summary>
    /// <param name="type">The type.</param>
    extension(Type type)
    {
        /// <summary>Retrieves the Slice type ID from this type if it has the attribute
        /// <see cref="SliceTypeIdAttribute"/>.</summary>
        /// <returns>The Slice type ID, or <see langword="null" /> if this type does not carry the
        /// <see cref="SliceTypeIdAttribute"/> attribute.
        /// </returns>
        public string? GetSliceTypeId()
        {
            object[] attributes = type.GetCustomAttributes(typeof(SliceTypeIdAttribute), false);
            return attributes.Length == 1 && attributes[0] is SliceTypeIdAttribute typeId ? typeId.Value : null;
        }

        /// <summary>Retrieves the compact Slice type ID from this type if it has the attribute
        /// <see cref="CompactSliceTypeIdAttribute"/>.</summary>
        /// <returns>The compact Slice type ID, or <see langword="null" /> if this type does not carry the
        /// <see cref="CompactSliceTypeIdAttribute"/> attribute.</returns>
        public int? GetCompactSliceTypeId()
        {
            object[] attributes = type.GetCustomAttributes(typeof(CompactSliceTypeIdAttribute), false);
            return attributes.Length == 1 && attributes[0] is CompactSliceTypeIdAttribute typeId ? typeId.Value : null;
        }
    }
}
