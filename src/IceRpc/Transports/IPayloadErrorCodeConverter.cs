// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>A multiplexed stream implementation uses this converter to convert exceptions it receives through
/// <see cref="PipeReader.Complete(Exception?)" /> and <see cref="PipeWriter.Complete(Exception?)" /> into error codes,
/// and to convert error codes received from the remote peer into payload exceptions. Each converter implementation is
/// protocol-specific.</summary>
/// <seealso cref="MultiplexedConnectionOptions.PayloadErrorCodeConverter" />
public interface IPayloadErrorCodeConverter
{
    /// <summary>Converts an error code carried by a multiplexed stream into a payload exception.</summary>
    /// <param name="errorCode">The error code to convert.</param>
    /// <returns>The corresponding payload exception.</returns>
    PayloadException? FromErrorCode(ulong errorCode);

    /// <summary>Converts an exception into an error code to be carried by a multiplexed stream.</summary>
    /// <param name="exception">The exception to convert.</param>
    /// <returns>The corresponding error code.</returns>
    ulong ToErrorCode(Exception? exception);
}
