// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.Slice;

/// <summary>Base class for classes defined in Slice.</summary>
public abstract class SliceClass
{
    /// <summary>Gets the unknown slices if the class has a preserved-slice base class and has been sliced-off
    /// during decoding.</summary>
    public ImmutableList<SliceInfo> UnknownSlices { get; internal set; } = ImmutableList<SliceInfo>.Empty;

    internal void Decode(ref SliceDecoder decoder) => DecodeCore(ref decoder);

    internal void Encode(ref SliceEncoder encoder) => EncodeCore(ref encoder);

    /// <summary>Decodes the properties of this instance.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    protected abstract void DecodeCore(ref SliceDecoder decoder);

    /// <summary>Encodes the properties of this instance.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    protected abstract void EncodeCore(ref SliceEncoder encoder);
}
