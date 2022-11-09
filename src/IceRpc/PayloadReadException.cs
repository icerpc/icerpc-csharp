// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc;

/// <summary>This exception is thrown by calls to <see cref="PipeReader.ReadAsync" /> on an
/// <see cref="IncomingFrame.Payload" /> and corresponds to write errors in the remote peer.</summary>
public class PayloadReadException : Exception
{
    /// <summary>Gets the error code carried by this exception.</summary>
    public PayloadReadErrorCode ErrorCode { get; }

    /// <summary>Constructs a payload read exception.</summary>
    /// <param name="errorCode">The payload read error code.</param>
    public PayloadReadException(PayloadReadErrorCode errorCode)
        : base($"{nameof(PayloadReadException)} {{ ErrorCode = {errorCode} }}") =>
        ErrorCode = errorCode;
}
