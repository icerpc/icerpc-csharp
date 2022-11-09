// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc;

/// <summary>This exception is used to an error to the payload writer in the remote peer via a call to
/// <see cref="PipeReader.Complete" /> on an <see cref="IncomingFrame.Payload" />.</summary>
public class PayloadCompleteException : Exception
{
    /// <summary>Gets the error code carried by this exception.</summary>
    public PayloadCompleteErrorCode ErrorCode { get; }

    /// <summary>Constructs a payload complete exception.</summary>
    /// <param name="errorCode">The payload complete error code.</param>
    public PayloadCompleteException(PayloadCompleteErrorCode errorCode)
        : base($"{nameof(PayloadCompleteException)} {{ ErrorCode = {errorCode} }}") =>
        ErrorCode = errorCode;
}
