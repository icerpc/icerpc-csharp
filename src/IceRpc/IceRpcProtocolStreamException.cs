// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc;

/// <summary>This exception represents an icerpc-specific error transmitted as an error code over a multiplexed stream.
/// The multiplexed stream APIs throw this exception when the error code carried by the stream does not map to another
/// another exception such as <see cref="OperationCanceledException"/>.</summary>
public class IceRpcProtocolStreamException : Exception
{
    /// <summary>Gets the error code carried by this exception.</summary>
    internal IceRpcStreamErrorCode ErrorCode { get; }

    /// <summary>Constructs a multiplexed stream exception.</summary>
    /// <param name="errorCode">The error code.</param>
    internal IceRpcProtocolStreamException(IceRpcStreamErrorCode errorCode)
        : base($"IceRpcProtocolStreamException: {errorCode}") =>
        ErrorCode = errorCode;
}
