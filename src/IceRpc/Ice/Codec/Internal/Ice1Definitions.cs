// Copyright (c) ZeroC, Inc.

namespace IceRpc.Ice.Codec.Internal;

/// <summary>Enumerations and constants used by Ice1.</summary>
internal static class Ice1Definitions
{
    internal const byte TagEndMarker = 0xFF;

    /// <summary>The first byte of each encoded class or exception slice.</summary>
    /// <remarks>The first 2 bits of IceFlags represent the TypeIdKind, which can be extracted using
    /// <see cref="IceFlagsExtensions.GetTypeIdKind" />.</remarks>
    [Flags]
    internal enum IceFlags : byte
    {
        TypeIdMask = 3,
        HasTaggedFields = 4,
        HasIndirectionTable = 8,
        HasIceSize = 16,
        IsLastIce = 32
    }

    /// <summary>The first 2 bits of the <see cref="IceFlags" />.</summary>
    internal enum TypeIdKind : byte
    {
        None = 0,
        String = 1,
        Index = 2,
        CompactId = 3,
    }
}

internal static class IceFlagsExtensions
{
    /// <summary>Extracts the <see cref="Ice1Definitions.TypeIdKind" /> of a <see cref="Ice1Definitions.IceFlags"
    /// /> value.</summary>
    /// <param name="sliceFlags">The <see cref="Ice1Definitions.IceFlags" /> value.</param>
    /// <returns>The <see cref="Ice1Definitions.TypeIdKind" /> encoded in sliceFlags.</returns>
    internal static Ice1Definitions.TypeIdKind GetTypeIdKind(this Ice1Definitions.IceFlags sliceFlags) =>
        (Ice1Definitions.TypeIdKind)(sliceFlags & Ice1Definitions.IceFlags.TypeIdMask);
}
