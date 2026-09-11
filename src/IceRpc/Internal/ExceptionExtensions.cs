// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

/// <summary>Provides extension methods for <see cref="Exception" /> that convert an exception thrown by a dispatch
/// into the failure response sent to the caller.</summary>
internal static class ExceptionExtensions
{
    /// <summary>Returns the status code of the failure response that an icerpc connection sends to the caller when
    /// the dispatch throws this exception.</summary>
    /// <param name="exception">The exception thrown by the dispatch.</param>
    /// <returns>The status code of the failure response.</returns>
    /// <remarks>The mapping of exceptions other than <see cref="DispatchException" /> is specific to the icerpc
    /// protocol: an ice connection maps only <see cref="InvalidDataException" />.</remarks>
    internal static StatusCode ToStatusCode(this Exception exception) =>
        exception switch
        {
            DispatchException { ConvertToInternalError: true } => StatusCode.InternalError,
            DispatchException dispatchException => dispatchException.StatusCode,
            InvalidDataException => StatusCode.InvalidData,
            NotSupportedException => StatusCode.NotSupported,
            IceRpcException { IceRpcError: IceRpcError.TruncatedData } => StatusCode.TruncatedPayload,
            _ => StatusCode.InternalError
        };

    /// <summary>Creates the failure response sent to the caller when the dispatch throws this exception.</summary>
    /// <param name="exception">The exception thrown by the dispatch.</param>
    /// <param name="request">The incoming request.</param>
    /// <returns>The failure response. Its status code is given by <see cref="ToStatusCode" />.</returns>
    internal static OutgoingResponse ToOutgoingResponse(this Exception exception, IncomingRequest request) =>
        exception is DispatchException { ConvertToInternalError: false } dispatchException ?
            new OutgoingResponse(
                request,
                dispatchException.StatusCode,
                dispatchException.Message,
                dispatchException.InnerException) :
            new OutgoingResponse(request, exception.ToStatusCode(), message: null, exception);
}
