// Copyright (c) ZeroC, Inc.

namespace IceRpc.Internal;

internal static class DispatchExceptionExtensions
{
    /// <summary>Extension methods for <see cref="DispatchException" />.</summary>
    /// <param name="exception">The dispatch exception.</param>
    extension(DispatchException exception)
    {
        /// <summary>Creates an outgoing response from this dispatch exception.</summary>
        /// <param name="request">The incoming request.</param>
        /// <returns>The outgoing response.</returns>
        internal OutgoingResponse ToOutgoingResponse(IncomingRequest request)
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
}
