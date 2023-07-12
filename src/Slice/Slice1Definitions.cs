// Copyright (c) ZeroC, Inc.

using System.Diagnostics.CodeAnalysis;

namespace Slice;

/// <summary>Enumerations and constants used by Slice1.</summary>
public static class Slice1Definitions
{
    /// <summary>A marker that indicates the end of a (possibly empty) sequence of tagged fields.</summary>
    public const byte TagEndMarker = 0xFF;

    /// <summary>The first byte of each encoded class or exception slice.</summary>
    /// <remarks>The first 2 bits of SliceFlags represent the <see cref="TypeIdKind"/>.</remarks>
    [Flags]
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute
    public enum SliceFlags : byte
#pragma warning restore CA2217 // Do not mark enums with FlagsAttribute
    {
        /// <summary>A mask to extract the <see cref="TypeIdKind"/>.</summary>
        TypeIdMask = 3,

        /// <summary>Whether or not the slice contains tagged fields.</summary>
        HasTaggedFields = 4,

        /// <summary>Whether or not the slice contains an indirection table.</summary>
        HasIndirectionTable = 8,

        /// <summary>Whether or not the slice size follows the type ID.</summary>
        HasSliceSize = 16,

        /// <summary>Whether or not this is the last slice.</summary>
        IsLastSlice = 32
    }

    /// <summary>The first 2 bits of the <see cref="SliceFlags" />.</summary>
    public enum TypeIdKind : byte
    {
        /// <summary>No type ID is encoded for the slice.</summary>
        None = 0,

        /// <summary>The type ID is encoded as a string.</summary>
        String = 1,

        /// <summary>The type ID is an index encoded as a size.</summary>
        Index = 2,

        /// <summary>The type ID is a compact ID encoded as a size.</summary>
        CompactId = 3,
    }
}
