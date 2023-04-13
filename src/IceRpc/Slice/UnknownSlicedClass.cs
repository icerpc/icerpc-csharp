// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace IceRpc.Slice;

/// <summary>Represents a fully sliced class instance. The local IceRPC runtime does not know this type or any of its
/// base classes (other than <see cref="SliceClass" />).</summary>
public sealed class UnknownSlicedClass : SliceClass
{
    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void DecodeCore(ref SliceDecoder decoder)
    {
    }

    /// <inheritdoc/>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void EncodeCore(ref SliceEncoder encoder) =>
        encoder.EncodeUnknownSlices(UnknownSlices, fullySliced: true);

    internal UnknownSlicedClass()
    {
    }
}
