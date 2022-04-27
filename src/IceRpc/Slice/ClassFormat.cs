// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>This enumeration describes the possible formats when encoding classes.</summary>
public enum ClassFormat
{
    /// <summary>The Compact format assumes the sender and receiver have the same Slice definitions for classes.
    /// If an application receives a derived class it does not know, it is not capable to decode it into a known base
    /// class because there is not enough information in the encoded payload. The Compact format is the default.
    /// </summary>
    Compact,

    /// <summary>The Sliced format allows slicing of unknown slices by the receiver. If an application receives a
    /// derived class it does not know, it can slice off the derived bits and create a base class.</summary>
    Sliced
}
