// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec.Internal;

/// <summary>Enumerations and constants used by the Ice encoding.</summary>
internal static class IceEncodingDefinitions
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
    /// <summary>Extracts the <see cref="IceEncodingDefinitions.TypeIdKind" /> of a <see cref="IceEncodingDefinitions.SliceFlags"
    /// /> value.</summary>
    /// <param name="sliceFlags">The <see cref="IceEncodingDefinitions.SliceFlags" /> value.</param>
    /// <returns>The <see cref="IceEncodingDefinitions.TypeIdKind" /> encoded in sliceFlags.</returns>
    internal static IceEncodingDefinitions.TypeIdKind GetTypeIdKind(this IceEncodingDefinitions.SliceFlags sliceFlags) =>
        (IceEncodingDefinitions.TypeIdKind)(sliceFlags & IceEncodingDefinitions.SliceFlags.TypeIdMask);
}
