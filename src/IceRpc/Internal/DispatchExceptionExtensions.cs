// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

internal static class DispatchExceptionExtensions
{
    /// <summary>Creates an outgoing response from this dispatch exception.</summary>
    /// <param name="exception">The dispatch excpetion.</param>
    /// <param name="request">The incoming request.</param>
    /// <returns>The outgoing response.</returns>
    internal static OutgoingResponse ToOutgoingResponse(this DispatchException exception, IncomingRequest request)
    {
        if (exception.ConvertToInternalError)
        {
            return new OutgoingResponse(request, StatusCode.InternalError, message: null, exception);
        }
        else
        {
            return new OutgoingResponse(request, exception.StatusCode, exception.Message, exception.InnerException);
        }
    }
}
