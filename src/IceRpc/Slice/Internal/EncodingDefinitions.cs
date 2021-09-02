// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    internal static class EncodingDefinitions
    {
        internal const long VarLongMinValue = -2_305_843_009_213_693_952; // -2^61
        internal const long VarLongMaxValue = 2_305_843_009_213_693_951; // 2^61 - 1
        internal const ulong VarULongMinValue = 0;
        internal const ulong VarULongMaxValue = 4_611_686_018_427_387_903; // 2^62 - 1
    }
}
