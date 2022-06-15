// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;

namespace IceRpc;

/// <summary>This exception represents an icerpc-specific error transmitted as an error code over a multiplexed stream.
/// The multiplexed stream APIs throw this exception when the error code carried by the stream does not map to another
/// another exception.</summary>
/// <seealso cref="Protocol.FromStreamErrorCode"/>
/// <seealso cref="Protocol.ToStreamErrorCode"/>
public class IceRpcProtocolStreamException : Exception
{
    /// <summary>Gets the error code carried by this exception.</summary>
    internal IceRpcStreamErrorCode ErrorCode { get; }

    /// <summary>Constructs an icerpc stream exception.</summary>
    /// <param name="errorCode">The error code.</param>
    internal IceRpcProtocolStreamException(IceRpcStreamErrorCode errorCode)
        : base($"{nameof(IceRpcProtocolStreamException)}: {errorCode}") =>
        ErrorCode = errorCode;
}
