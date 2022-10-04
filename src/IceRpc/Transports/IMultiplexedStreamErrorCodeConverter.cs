// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>A multiplexed stream implementation uses this interface to convert exceptions it receives through
/// <see cref="PipeReader.Complete(Exception?)" />, <see cref="PipeWriter.Complete(Exception?)" /> and similar APIs
/// into error codes, and to convert error codes sent by the remote peer into exceptions.</summary>
public interface IMultiplexedStreamErrorCodeConverter
{
    /// <summary>Converts a transport-independent error code carried by a multiplexed stream into an exception.
    /// </summary>
    /// <param name="errorCode">The errorCode to convert.</param>
    /// <returns>The corresponding exception.</returns>
    Exception? FromErrorCode(ulong errorCode);

    /// <summary>Converts an exception into an error code to be carried by a multiplexed stream.</summary>
    /// <param name="exception">The exception to convert.</param>
    /// <returns>The corresponding error code.</returns>
    ulong ToErrorCode(Exception? exception);
}
