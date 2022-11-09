// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports;

/// <summary>A multiplexed stream implementation uses this converter to convert exceptions it receives through
/// <see cref="PipeReader.Complete" /> and <see cref="PipeWriter.Complete" /> into error codes, and to convert error
/// codes received from the remote peer into exceptions thrown by <see cref="PipeReader.ReadAsync" />,
/// <see cref="PipeWriter.FlushAsync" /> and <see cref="PipeWriter.WriteAsync" />.</summary>
/// <seealso cref="MultiplexedConnectionOptions.MultiplexedStreamExceptionConverter" />
public interface IMultiplexedStreamExceptionConverter
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

    /// <summary>Converts an error code into a payload complete exception thrown by a call to
    /// <see cref="PipeWriter.FlushAsync" /> or <see cref="PipeWriter.WriteAsync" /> on a stream output.</summary>
    /// <param name="errorCode">The error code to convert.</param>
    /// <returns>The corresponding payload complete exception, or <see langword="null" />.</returns>
    PayloadCompleteException? ToFlushException(ulong errorCode);

    /// <summary>Converts an error code into a payload read exception thrown by a call to
    /// <see cref="PipeReader.ReadAsync" /> on a stream input.</summary>
    /// <param name="errorCode">The error code to convert.</param>
    /// <returns>The corresponding payload read exception.</returns>
    PayloadReadException ToReadException(ulong errorCode);
}
