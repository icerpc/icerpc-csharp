// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>A multiplexed stream implementation uses this converter to convert exceptions it receives through
/// <see cref="PipeReader.Complete(Exception?)" /> and <see cref="PipeWriter.Complete(Exception?)" /> into error codes,
/// and to convert error codes received from the remote peer into payload exceptions. Each converter implementation is
/// protocol-specific.</summary>
/// <seealso cref="MultiplexedConnectionOptions.PayloadExceptionConverter" />
public interface IPayloadExceptionConverter
{
    /// <summary>Converts an exception into an error code carried by a multiplexed stream. This exception is
    /// transmitted to <see cref="PipeReader.Complete(Exception?)" /> when called on <see cref="IDuplexPipe.Input" />.
    /// </summary>
    /// <param name="exception">The exception to convert.</param>
    /// <returns>The corresponding error code.</returns>
    ulong FromInputCompleteException(Exception? exception);

    /// <summary>Converts an exception into an error code carried by a multiplexed stream. This exception is
    /// transmitted to <see cref="PipeWriter.Complete(Exception?)" /> when called on <see cref="IDuplexPipe.Output" />.
    /// </summary>
    /// <param name="exception">The exception to convert.</param>
    /// <returns>The corresponding error code.</returns>
    ulong FromOutputCompleteException(Exception exception);

    /// <summary>Converts an error code received by a multiplexed stream output into a payload complete exception.
    /// </summary>
    /// <param name="errorCode">The error code to convert into a payload complete exception.</param>
    /// <returns>The corresponding payload complete exception.</returns>
    PayloadCompleteException? ToPayloadCompleteException(ulong errorCode);

    /// <summary>Converts an error code received by a multiplexed stream input into a payload read exception.</summary>
    /// <param name="errorCode">The error code to convert into a payload read exception.</param>
    /// <returns>The corresponding payload read exception.</returns>
    PayloadReadException ToPayloadReadException(ulong errorCode);
}
