// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice;

/// <summary>UnknownSlicedClass represents a fully sliced class instance. The local IceRPC runtime does not know
/// this type or any of its base classes (other than SliceClass).</summary>
public sealed class UnknownSlicedClass : SliceClass
{
    /// <inheritdoc/>
    protected override void DecodeCore(ref SliceDecoder decoder)
    {
    }

    /// <inheritdoc/>
    protected override void EncodeCore(ref SliceEncoder encoder) =>
        encoder.EncodeUnknownSlices(UnknownSlices, fullySliced: true);

    internal UnknownSlicedClass()
    {
    }
}
