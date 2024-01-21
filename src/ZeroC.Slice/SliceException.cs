// Copyright (c) ZeroC, Inc.

using System.ComponentModel;

namespace ZeroC.Slice;

/// <summary>Represents the base class for exceptions defined in Slice.</summary>
public abstract class SliceException : Exception
{
    /// <summary>Encodes this exception.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    public void Encode(ref SliceEncoder encoder) => EncodeCore(ref encoder);

    internal void Decode(ref SliceDecoder decoder) => DecodeCore(ref decoder);

    /// <summary>Constructs a Slice exception with the provided message and inner exception.</summary>
    /// <param name="message">A message that describes the exception.</param>
    /// <param name="innerException">The inner exception.</param>
    protected SliceException(string? message = null, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    /// <summary>Decodes a Slice exception.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void DecodeCore(ref SliceDecoder decoder) => throw new NotImplementedException();

    /// <summary>Encodes this Slice exception.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected abstract void EncodeCore(ref SliceEncoder encoder);
}
