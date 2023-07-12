// Copyright (c) ZeroC, Inc.

namespace Slice;

/// <summary>Describes the versions of the Slice encoding supported by this implementation.</summary>
public enum SliceEncoding : byte
{
    /// <summary>Slice encoding version 1. It's identical to the Ice encoding version 1.1.</summary>
    Slice1 = 1,

    /// <summary>Slice encoding version 2..</summary>
    Slice2 = 2,
}
