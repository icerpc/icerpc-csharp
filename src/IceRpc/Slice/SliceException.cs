// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>Base class for exceptions defined in Slice.</summary>
public abstract class SliceException : DispatchException, ITrait
{
    /// <summary>Encodes this exception.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    public void Encode(ref SliceEncoder encoder) => EncodeCore(ref encoder);

    /// <summary>Encodes this exception as a trait, by encoding its Slice type ID followed by its fields.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <remarks>Implemented only by Slice2-compatible exceptions.</remarks>
    public virtual void EncodeTrait(ref SliceEncoder encoder) => throw new NotImplementedException();

    internal void Decode(ref SliceDecoder decoder) => DecodeCore(ref decoder);

    /// <summary>Constructs a Slice exception with the default system message.</summary>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    protected SliceException(RetryPolicy? retryPolicy = null)
        : base(StatusCode.ApplicationError, retryPolicy)
    {
    }

    /// <summary>Constructs a Slice exception with the provided message and inner exception.</summary>
    /// <param name="message">Message that describes the exception.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    /// <param name="innerException">The inner exception.</param>
    protected SliceException(
        string? message,
        Exception? innerException = null,
        RetryPolicy? retryPolicy = null)
        : base(StatusCode.ApplicationError, message, innerException, retryPolicy)
    {
    }

    /// <summary>Constructs a Slice exception using a decoder.</summary>
    /// <param name="decoder">The decoder.</param>
    protected SliceException(ref SliceDecoder decoder)
        : base(
            StatusCode.ApplicationError,
            message: decoder.Encoding == SliceEncoding.Slice1 ? null : decoder.DecodeString()) =>
        ConvertToUnhandled = true;

    /// <summary>Constructs a Slice exception using a decoder and message.</summary>
    /// <param name="decoder">The decoder.</param>
    /// <param name="message">The message.</param>
    protected SliceException(ref SliceDecoder decoder, string message)
        : base(StatusCode.ApplicationError, message) =>
        ConvertToUnhandled = true;

    /// <summary>Decodes a Slice exception.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <remarks>Implemented only by Slice1-compatible exceptions.</remarks>
    protected virtual void DecodeCore(ref SliceDecoder decoder) => throw new NotImplementedException();

    /// <summary>Encodes this Slice exception.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <remarks>Implemented for all Slice encodings.</remarks>
    protected abstract void EncodeCore(ref SliceEncoder encoder);
}
