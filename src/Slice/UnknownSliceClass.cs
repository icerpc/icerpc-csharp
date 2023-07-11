// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace Slice;

/// <summary>Represents a fully sliced class instance. The <see cref="IActivator"/> used during decoding does not know
/// this type or any of its base classes.</summary>
public sealed class UnknownSliceClass : SliceClass
{
    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void DecodeCore(ref SliceDecoder decoder)
    {
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void EncodeCore(ref SliceEncoder encoder)
    {
    }

    internal UnknownSliceClass()
    {
    }
}
