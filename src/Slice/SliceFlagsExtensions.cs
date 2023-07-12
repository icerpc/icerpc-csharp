// Copyright (c) ZeroC, Inc.

namespace Slice;

/// <summary>This class provides an extension methods to extract the <see cref="Slice1Definitions.TypeIdKind" /> of a
/// <see cref="Slice1Definitions.SliceFlags" /> value.</summary>
public static class SliceFlagsExtensions
{
    /// <summary>Extracts the <see cref="Slice1Definitions.TypeIdKind" /> of a <see cref="Slice1Definitions.SliceFlags"
    /// /> value.</summary>
    /// <param name="sliceFlags">The <see cref="Slice1Definitions.SliceFlags" /> value.</param>
    /// <returns>The <see cref="Slice1Definitions.TypeIdKind" /> encoded in sliceFlags.</returns>
    public static Slice1Definitions.TypeIdKind GetTypeIdKind(this Slice1Definitions.SliceFlags sliceFlags) =>
        (Slice1Definitions.TypeIdKind)(sliceFlags & Slice1Definitions.SliceFlags.TypeIdMask);
}
