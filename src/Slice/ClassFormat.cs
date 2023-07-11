// Copyright (c) ZeroC, Inc.

namespace Slice;

/// <summary>Describes the possible formats when encoding classes.</summary>
public enum ClassFormat
{
    /// <summary>The Compact format assumes the sender and receiver have the same Slice definitions for classes. If an
    /// application receives a derived class it does not know, it is not capable of decoding it into a known base
    /// class because there is not enough information in the encoded payload. The Compact format is the default.
    /// </summary>
    Compact,

    /// <summary>The Sliced format allows the receiver to slice off unknown slices. If an application receives a
    /// derived class it does not know, it can create a base class while preserving the unknown derived slices.
    /// </summary>
    Sliced
}
