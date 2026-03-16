// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using System.ComponentModel;

namespace IceRpc.Ice;

/// <summary>Represents the base class for exceptions defined in Ice.</summary>
public abstract class IceException : Exception
{
    // Uses the default parameterless constructor.

    /// <summary>Encodes this exception.</summary>
    /// <param name="encoder">The Ice encoder.</param>
    public void Encode(ref IceEncoder encoder) => EncodeCore(ref encoder);

    internal void Decode(ref IceDecoder decoder) => DecodeCore(ref decoder);

    /// <summary>Decodes an Ice exception.</summary>
    /// <param name="decoder">The Ice decoder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected abstract void DecodeCore(ref IceDecoder decoder);

    /// <summary>Encodes this Ice exception.</summary>
    /// <param name="encoder">The Ice encoder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected abstract void EncodeCore(ref IceEncoder encoder);
}
