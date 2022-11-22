// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>Base class for exceptions defined in Slice.</summary>
public abstract class SliceException : DispatchException
{
    /// <summary>Encodes this exception.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    public void Encode(ref SliceEncoder encoder) => EncodeCore(ref encoder);

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

    /// <summary>Decodes a Slice exception.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <remarks>Implemented only by Slice1-compatible exceptions.</remarks>
    protected virtual void DecodeCore(ref SliceDecoder decoder) => throw new NotImplementedException();

    /// <summary>Encodes this Slice exception.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <remarks>Implemented for all Slice encodings.</remarks>
    protected abstract void EncodeCore(ref SliceEncoder encoder);
}
