// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A delegate that reads the request parameters from a request frame.</summary>
    /// <typeparam name="T">The type of the request parameters to read.</typeparam>
    /// <param name="request">The request frame to read the parameters from.</param>
    /// <returns>The request parameters.</returns>
    public delegate T RequestReader<T>(IncomingRequest request);

    /// <summary>The base interface for all services.</summary>
    public interface IService : IDispatcher
    {
        // The following are helper classes and methods for generated servants.

        /// <summary>Holds a <see cref="RequestReader{T}"/> for each remote operation with parameter(s) defined in
        /// the pseudo-interface Service.</summary>
        public static class Request
        {
            /// <summary>The <see cref="RequestReader{T}"/> for the parameter of operation ice_isA.</summary>
            /// <summary>Decodes the ice_id operation parameters from an <see cref="IncomingRequest"/>.</summary>
            /// <param name="request">The request frame.</param>
            /// <returns>The return value decoded from the frame.</returns>
            public static string IceIsA(IncomingRequest request) =>
                 request.ReadArgs(InputStream.IceReaderIntoString);
        }

        /// <summary>Provides an <see cref="OutgoingResponse"/> factory method for each non-void remote operation
        /// defined in the pseudo-interface Object.</summary>
        public static class Response
        {
            /// <summary>Creates an <see cref="OutgoingResponse"/> for operation ice_id.</summary>
            /// <param name="dispatch">Holds decoded header data and other information about the current request.</param>
            /// <param name="returnValue">The return value to write into the new frame.</param>
            /// <returns>A new <see cref="OutgoingResponse"/>.</returns>
            public static OutgoingResponse IceId(Dispatch dispatch, string returnValue) =>
                OutgoingResponse.WithReturnValue(
                    dispatch,
                    compress: false,
                    format: default,
                    returnValue,
                    OutputStream.IceWriterFromString);

            /// <summary>Creates an <see cref="OutgoingResponse"/> for operation ice_ids.</summary>
            /// <param name="dispatch">Holds decoded header data and other information about the current request.</param>
            /// <param name="returnValue">The return value to write into the new frame.</param>
            /// <returns>A new <see cref="OutgoingResponse"/>.</returns>
            public static OutgoingResponse IceIds(Dispatch dispatch, IEnumerable<string> returnValue) =>
                OutgoingResponse.WithReturnValue(
                    dispatch,
                    compress: false,
                    format: default,
                    returnValue,
                    (ostr, returnValue) => ostr.WriteSequence(returnValue, OutputStream.IceWriterFromString));

            /// <summary>Creates an <see cref="OutgoingResponse"/> for operation ice_isA.</summary>
            /// <param name="dispatch">Holds decoded header data and other information about the current request.</param>
            /// <param name="returnValue">The return value to write into the new frame.</param>
            /// <returns>A new <see cref="OutgoingResponse"/>.</returns>
            public static OutgoingResponse IceIsA(Dispatch dispatch, bool returnValue) =>
                OutgoingResponse.WithReturnValue(
                    dispatch,
                    compress: false,
                    format: default,
                    returnValue,
                    OutputStream.IceWriterFromBool);
        }

        /// <summary>Returns the Slice type ID of the most-derived interface supported by this object.</summary>
        /// <param name="dispatch">The Current object for the dispatch.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is cancelled.
        /// </param>
        /// <returns>The Slice type ID of the most-derived interface.</returns>
        public ValueTask<string> IceIdAsync(Dispatch dispatch, CancellationToken cancel) => new("::Ice::Object");

        /// <summary>Returns the Slice type IDs of the interfaces supported by this object.</summary>
        /// <param name="dispatch">The Current object for the dispatch.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The Slice type IDs of the interfaces supported by this object, in alphabetical order.</returns>
        public ValueTask<IEnumerable<string>> IceIdsAsync(Dispatch dispatch, CancellationToken cancel) =>
            new(new string[] { "::Ice::Object" });

        /// <summary>Tests whether this service supports the specified Slice interface.</summary>
        /// <param name="typeId">The type ID of the Slice interface to test against.</param>
        /// <param name="dispatch">The Current object for the dispatch.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>True if this object implements the interface specified by typeId.</returns>
        public async ValueTask<bool> IceIsAAsync(string typeId, Dispatch dispatch, CancellationToken cancel)
        {
            var array = (string[])await IceIdsAsync(dispatch, cancel).ConfigureAwait(false);
            return Array.BinarySearch(array, typeId, StringComparer.Ordinal) >= 0;
        }

        /// <summary>Tests whether this object can be reached.</summary>
        /// <param name="dispatch">The Current object for the dispatch.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        public ValueTask IcePingAsync(Dispatch dispatch, CancellationToken cancel) => default;

        /// <summary>The generated code calls this method to ensure that when an operation is _not_ declared
        /// idempotent, the request is not marked idempotent. If the request is marked idempotent, it means the caller
        /// incorrectly believes this operation is idempotent.</summary>
        /// <param name="request">The current request.</param>
        protected static void IceCheckNonIdempotent(IncomingRequest request)
        {
            if (request.IsIdempotent)
            {
                throw new InvalidDataException(
                        $@"idempotent mismatch for operation '{request.Operation
                        }': received request marked idempotent for a non-idempotent operation");
            }
        }

        /// <summary>Dispatches an ice_id request.</summary>
        /// <param name="request">The current request.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        protected async ValueTask<OutgoingResponse> IceDIceIdAsync(
            IncomingRequest request,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            request.ReadEmptyArgs();
            string returnValue = await IceIdAsync(dispatch, cancel).ConfigureAwait(false);
            return Response.IceId(dispatch, returnValue);
        }

        /// <summary>Dispatches an ice_ids request.</summary>
        /// <param name="request">The current request.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        protected async ValueTask<OutgoingResponse> IceDIceIdsAsync(
            IncomingRequest request,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            request.ReadEmptyArgs();
            IEnumerable<string> returnValue = await IceIdsAsync(dispatch, cancel).ConfigureAwait(false);
            return Response.IceIds(dispatch, returnValue);
        }

        /// <summary>Dispatches an ice_isA request.</summary>
        /// <param name="request">The current request.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        protected async ValueTask<OutgoingResponse> IceDIceIsAAsync(
            IncomingRequest request, Dispatch dispatch, CancellationToken cancel)
        {
            string id = Request.IceIsA(request);
            bool returnValue = await IceIsAAsync(id, dispatch, cancel).ConfigureAwait(false);
            return Response.IceIsA(dispatch, returnValue);
        }

        /// <summary>Dispatches an ice_ping request.</summary>
        /// <param name="request">The current request.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        protected async ValueTask<OutgoingResponse> IceDIcePingAsync(
            IncomingRequest request, Dispatch dispatch, CancellationToken cancel)
        {
            request.ReadEmptyArgs();
            await IcePingAsync(dispatch, cancel).ConfigureAwait(false);
            return OutgoingResponse.WithVoidReturnValue(dispatch);
        }

        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            var dispatch = new Dispatch(request);
            try
            {
                return await DispatchAsync(request, dispatch, cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // The client requested cancellation, we log it and let it propagate.
                // Socket.Logger.LogDispatchCanceledByClient(request);
                throw;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException &&
                    request.Connection.Server is Server server &&
                    server.CancelDispatch.IsCancellationRequested)
                {
                    // Replace exception
                    ex = new ServerException("dispatch canceled by server shutdown");
                }
                // else it's another OperationCanceledException that the implementation should have caught, and it
                // will become an UnhandledException below.

                if (request.IsOneway)
                {
                    // We log this exception, since otherwise it would be lost.
                    // Socket.Logger.LogDispatchException(request, ex);
                    return OutgoingResponse.WithVoidReturnValue(new Dispatch(request));
                }
                else
                {
                    RemoteException actualEx;
                    if (ex is RemoteException remoteEx && !remoteEx.ConvertToUnhandled)
                    {
                        actualEx = remoteEx;
                    }
                    else
                    {
                        actualEx = new UnhandledException(ex);

                        // We log the "source" exception as UnhandledException may not include all details.
                        // Socket.Logger.LogDispatchException(request, ex);
                    }
                    return new OutgoingResponse(new Dispatch(request), actualEx);
                }
            }
        }

        public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, Dispatch dispatch, CancellationToken cancel);
    }
}
