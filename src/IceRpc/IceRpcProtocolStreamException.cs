// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;

namespace IceRpc;

/// <summary>This exception represents an icerpc-specific error transmitted as an error code over a multiplexed stream.
/// The multiplexed stream APIs throw this exception when the error code carried by the stream does not map to another
/// exception.</summary>
/// <seealso cref="IMultiplexedStreamErrorCodeConverter"/>
public class IceRpcProtocolStreamException : Exception
{
    /// <summary>Gets the error code carried by this exception.</summary>
    internal IceRpcStreamErrorCode ErrorCode { get; }

    /// <summary>Constructs an icerpc stream exception.</summary>
    /// <param name="errorCode">The error code.</param>
    internal IceRpcProtocolStreamException(IceRpcStreamErrorCode errorCode)
        : base($"{nameof(IceRpcProtocolStreamException)} {{ ErrorCode = {errorCode} }}") =>
        ErrorCode = errorCode;
}
