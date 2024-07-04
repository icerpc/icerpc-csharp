// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace ZeroC.Slice;

/// <summary>Represents the base class for exceptions defined in Slice.</summary>
public abstract class SliceException : Exception
{
    // Uses the default parameterless constructor.

    /// <summary>Encodes this exception.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    public void Encode(ref SliceEncoder encoder) => EncodeCore(ref encoder);

    internal void Decode(ref SliceDecoder decoder) => DecodeCore(ref decoder);

    /// <summary>Decodes a Slice exception.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected abstract void DecodeCore(ref SliceDecoder decoder);

    /// <summary>Encodes this Slice exception.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected abstract void EncodeCore(ref SliceEncoder encoder);
}
