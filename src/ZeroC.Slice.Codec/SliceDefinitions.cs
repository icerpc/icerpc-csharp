// Copyright (c) ZeroC, Inc.

namespace ZeroC.Slice.Codec;

/// <summary>Provides a constant used by the Slice encoding.</summary>
public static class SliceDefinitions
{
    /// <summary>A marker that indicates the end of a (possibly empty) sequence of tagged fields.</summary>
    public const int TagEndMarker = -1;
}
