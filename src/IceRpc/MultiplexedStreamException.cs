// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>This exception is used to transmit an error code through a multiplexed stream. It is thrown by the
/// multiplexed stream APIs to report the error code transmitted by the remote peer when this error code does not map to
/// another exception such as <see cref="OperationCanceledException"/>.</summary>
public class MultiplexedStreamException : Exception
{
    /// <summary>Gets the error code carried by this exception.</summary>
    public MultiplexedStreamErrorCode ErrorCode { get; }

    /// <summary>Constructs a multiplexed stream exception.</summary>
    /// <param name="errorCode">The error code.</param>
    public MultiplexedStreamException(MultiplexedStreamErrorCode errorCode)
        : base($"MultiplexedStreamException: {errorCode}") =>
        ErrorCode = errorCode;
}
