// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Describes the known versions of the Slice encoding.</summary>
/// <remarks>Keep this enumeration in sync with the <c>ZeroC.Slice.SliceEncoding</c> enumeration defined in the
/// <c>ZeroC.Slice</c> assembly.</remarks>
internal enum SliceEncoding : byte
{
    /// <summary>Slice encoding version 1. It's identical to the Ice encoding version 1.1.</summary>
    Slice1 = 1,

    /// <summary>Slice encoding version 2.</summary>
    Slice2 = 2,
}
