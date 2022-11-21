// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice;

/// <summary>Base class for exceptions defined in Slice.</summary>
public abstract class SliceException : Exception, ITrait
{
    /// <inheritdoc/>
    public override string Message => _hasCustomMessage || DefaultMessage is null ? base.Message : DefaultMessage;

    /// <summary>Gets or sets a value indicating whether the exception should be converted into a <see
    /// cref="DispatchException" /> with status code <see cref="StatusCode.UnhandledException" /> when thrown from a
    /// dispatch.</summary>
    /// <value>When <see langword="true" />, this exception is converted into dispatch exception with status code
    /// <see cref="StatusCode.UnhandledException" /> just before it's encoded. The default value is
    /// <see langword="true" /> for an exception decoded from <see cref="IncomingResponse" />, and
    /// <see langword="false" /> for an exception created by the application using a constructor of this exception.
    /// </value>
    public bool ConvertToUnhandled { get; set; }

    /// <summary>Gets the Slice exception retry policy.</summary>
    public RetryPolicy RetryPolicy { get; } = RetryPolicy.NoRetry;

    /// <summary>Gets the exception default message. When not null and the application does not construct the
    /// exception with a constructor that takes a message parameter, Message returns DefaultMessage. This property
    /// should be overridden in derived partial exception classes that provide a custom default message.</summary>
    protected virtual string? DefaultMessage => null;

    private readonly bool _hasCustomMessage;

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
    protected SliceException(RetryPolicy? retryPolicy = null) => RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;

    /// <summary>Constructs a Slice exception with the provided message and inner exception.</summary>
    /// <param name="message">Message that describes the exception.</param>
    /// <param name="retryPolicy">The retry policy for the exception.</param>
    /// <param name="innerException">The inner exception.</param>
    protected SliceException(
        string? message,
        Exception? innerException = null,
        RetryPolicy? retryPolicy = null)
        : base(message, innerException)
    {
        RetryPolicy = retryPolicy ?? RetryPolicy.NoRetry;
        _hasCustomMessage = message is not null;
    }

    /// <summary>Constructs a Slice exception using a decoder.</summary>
    /// <param name="decoder">The decoder.</param>
    protected SliceException(ref SliceDecoder decoder)
        : base(decoder.Encoding == SliceEncoding.Slice1 ? null : decoder.DecodeString())
    {
        _hasCustomMessage = decoder.Encoding != SliceEncoding.Slice1;
        ConvertToUnhandled = true;
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
