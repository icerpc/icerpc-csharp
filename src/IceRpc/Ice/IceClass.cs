// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using System.Collections.Immutable;
using System.ComponentModel;

namespace IceRpc.Ice;

/// <summary>Represents the base class for classes defined in Ice. The Ice keyword AnyClass maps to this class.
/// </summary>
public abstract class IceClass
{
    /// <summary>Gets the unknown slices if the class has a preserved-slice base class and has been sliced-off
    /// during decoding.</summary>
    public ImmutableList<SliceInfo> UnknownIces { get; internal set; } = ImmutableList<SliceInfo>.Empty;

    internal void Decode(ref IceDecoder decoder) => DecodeCore(ref decoder);

    internal void Encode(ref IceEncoder encoder) => EncodeCore(ref encoder);

    /// <summary>Decodes the properties of this instance.</summary>
    /// <param name="decoder">The Ice decoder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected abstract void DecodeCore(ref IceDecoder decoder);

    /// <summary>Encodes the properties of this instance.</summary>
    /// <param name="encoder">The Ice encoder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected abstract void EncodeCore(ref IceEncoder encoder);
}
