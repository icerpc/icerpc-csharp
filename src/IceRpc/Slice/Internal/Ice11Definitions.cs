// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    internal static class Ice11Definitions
    {
        internal const byte TagEndMarker = 0xFF;

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
        internal static Ice11Definitions.TypeIdKind GetTypeIdKind(this Ice11Definitions.SliceFlags sliceFlags) =>
            (Ice11Definitions.TypeIdKind)(sliceFlags & Ice11Definitions.SliceFlags.TypeIdMask);
    }
}
