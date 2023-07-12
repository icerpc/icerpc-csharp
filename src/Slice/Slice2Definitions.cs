// Copyright (c) ZeroC, Inc.

namespace Slice;

/// <summary>Provides a constant used by the Slice2 encoding.</summary>
public static class Slice2Definitions
{
    /// <summary>A marker that indicates the end of a (possibly empty) sequence of tagged fields.</summary>
    public const int TagEndMarker = -1;

    /// <summary>Represents the smallest possible value of an Slice <c>varint62</c>. This field is constant.</summary>
    public const long VarInt62MinValue = -2_305_843_009_213_693_952; // -2^61

    /// <summary>Represents the largest possible value of an Slice <c>varint62</c>. This field is constant.</summary>
    public const long VarInt62MaxValue = 2_305_843_009_213_693_951; // 2^61 - 1

    /// <summary>Represents the smallest possible value of an Slice <c>varuint62</c>. This field is constant.</summary>
    public const ulong VarUInt62MinValue = 0;

    /// <summary>Represents the largest possible value of an Slice <c>varuint62</c>. This field is constant.</summary>
    public const ulong VarUInt62MaxValue = 4_611_686_018_427_387_903; // 2^62 - 1
}
