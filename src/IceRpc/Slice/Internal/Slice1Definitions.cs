// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Internal;

/// <summary>Enumerations and constants used by Slice1.</summary>
internal static class Slice1Definitions
{
    internal const byte TagEndMarker = 0xFF;

    /// <summary>The first byte of each encoded class or exception slice.</summary>
    /// <remarks>The first 2 bits of SliceFlags represent the TypeIdKind, which can be extracted using
    /// <see cref="SliceFlagsExtensions.GetTypeIdKind" />.</remarks>
    [Flags]
    internal enum SliceFlags : byte
    {
        TypeIdMask = 3,
        HasTaggedFields = 4,
        HasIndirectionTable = 8,
        HasSliceSize = 16,
        IsLastSlice = 32
    }

    /// <summary>The first 2 bits of the <see cref="SliceFlags" />.</summary>
    internal enum TypeIdKind : byte
    {
        None = 0,
        String = 1,
        Index = 2,
        CompactId = 3,
    }
}

internal static class SliceFlagsExtensions
{
    /// <summary>Extracts the <see cref="Slice1Definitions.TypeIdKind" /> of a <see cref="Slice1Definitions.SliceFlags"
    /// /> value.</summary>
    /// <param name="sliceFlags">The <see cref="Slice1Definitions.SliceFlags" /> value.</param>
    /// <returns>The <see cref="Slice1Definitions.TypeIdKind" /> encoded in sliceFlags.</returns>
    internal static Slice1Definitions.TypeIdKind GetTypeIdKind(this Slice1Definitions.SliceFlags sliceFlags) =>
        (Slice1Definitions.TypeIdKind)(sliceFlags & Slice1Definitions.SliceFlags.TypeIdMask);
}
