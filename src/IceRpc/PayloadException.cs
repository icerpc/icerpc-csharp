// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>This exception reports an error when reading an <see cref="IncomingFrame.Payload" /> or when sending a
/// payload (such as <see cref="OutgoingFrame.Payload" />) to a remote peer.</summary>
public class PayloadException : Exception
{
    /// <summary>Gets the error code carried by this exception.</summary>
    public PayloadErrorCode ErrorCode { get; }

    /// <summary>Constructs a payload exception.</summary>
    /// <param name="errorCode">The payload error code.</param>
    public PayloadException(PayloadErrorCode errorCode)
        : base($"{nameof(PayloadException)} {{ ErrorCode = {errorCode} }}") =>
        ErrorCode = errorCode;
}
