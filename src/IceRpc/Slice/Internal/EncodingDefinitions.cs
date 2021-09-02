// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    internal static class EncodingDefinitions
    {
        internal const long VarLongMinValue = -2_305_843_009_213_693_952; // -2^61
        internal const long VarLongMaxValue = 2_305_843_009_213_693_951; // 2^61 - 1
        internal const ulong VarULongMinValue = 0;
        internal const ulong VarULongMaxValue = 4_611_686_018_427_387_903; // 2^62 - 1

        internal const byte TaggedEndMarker = 0xFF;

        /// <summary>The first byte of each encoded class or exception slice.</summary>
        [Flags]
        internal enum SliceFlags : byte
        {
            /// <summary>The first 2 bits of SliceFlags represent the TypeIdKind, which can be extracted using
            /// GetTypeIdKind.</summary>
            TypeIdMask = 3,
            HasTaggedMembers = 4,
            HasIndirectionTable = 8,
            HasSliceSize = 16,
            IsLastSlice = 32
        }

        /// <summary>The first 2 bits of the SliceFlags.</summary>
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
        /// <summary>Extracts the TypeIdKind of a SliceFlags value.</summary>
        /// <param name="sliceFlags">The SliceFlags value.</param>
        /// <returns>The TypeIdKind encoded in sliceFlags.</returns>
        internal static EncodingDefinitions.TypeIdKind GetTypeIdKind(this EncodingDefinitions.SliceFlags sliceFlags) =>
            (EncodingDefinitions.TypeIdKind)(sliceFlags & EncodingDefinitions.SliceFlags.TypeIdMask);
    }
}
