// Copyright (c) ZeroC, Inc.

namespace IceRpc;

/// <summary>Represents an exception thrown while dispatching a request. It's encoded as a response with a status code
/// greater than <see cref="StatusCode.Ok" />.</summary>
public sealed class DispatchException : Exception
{
    /// <summary>Gets or sets a value indicating whether the exception should be converted into a <see
    /// cref="DispatchException" /> with status code <see cref="StatusCode.InternalError" /> when thrown from a
    /// dispatch.</summary>
    /// <value>When <see langword="true" />, this exception is converted into dispatch exception with status code <see
    /// cref="StatusCode.InternalError" /> just before it's encoded. Defaults to <see langword="true" /> for an
    /// exception decoded from an <see cref="IncomingResponse" />, and <see langword="false" /> for an exception created
    /// by the application using a constructor of <see cref="DispatchException" />.</value>
    public bool ConvertToInternalError { get; set; }

    /// <summary>Gets the status code.</summary>
    /// <value>The <see cref="IceRpc.StatusCode" /> of this exception.</value>
    public StatusCode StatusCode { get; }

    /// <summary>Constructs a new instance of <see cref="DispatchException" />.</summary>
    /// <param name="statusCode">The status code of this exception. It must be greater than <see
    /// cref="StatusCode.Ok" />.</param>
    /// <param name="message">A message that describes the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="statusCode" /> is equal to <see
    /// cref="StatusCode.Ok" />.</exception>
    public DispatchException(
        StatusCode statusCode,
        string? message = null,
        Exception? innerException = null)
        : base(message ?? $"The dispatch failed with status code {statusCode}.", innerException) =>
        StatusCode = statusCode > StatusCode.Ok ? statusCode :
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                $"The status code of a {nameof(DispatchException)} must be greater than {nameof(StatusCode.Ok)}.");

    /// <summary>Converts an exception thrown by a dispatch into a dispatch exception.</summary>
    /// <param name="exception">The exception thrown by the dispatch.</param>
    /// <returns><paramref name="exception" /> when it is a <see cref="DispatchException" />; otherwise, a new dispatch
    /// exception with <paramref name="exception" /> as its inner exception and a status code that depends on the type
    /// of <paramref name="exception" />: <see cref="StatusCode.InvalidData" /> for an
    /// <see cref="InvalidDataException" />, <see cref="StatusCode.NotSupported" /> for a
    /// <see cref="NotSupportedException" />, <see cref="StatusCode.TruncatedPayload" /> for an
    /// <see cref="IceRpcException" /> with error <see cref="IceRpcError.TruncatedData" />, and
    /// <see cref="StatusCode.InternalError" /> for any other exception.</returns>
    public static DispatchException FromException(Exception exception) =>
        exception as DispatchException ?? new DispatchException(
            exception switch
            {
                InvalidDataException => StatusCode.InvalidData,
                NotSupportedException => StatusCode.NotSupported,
                IceRpcException { IceRpcError: IceRpcError.TruncatedData } => StatusCode.TruncatedPayload,
                _ => StatusCode.InternalError
            },
            innerException: exception);
}
