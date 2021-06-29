// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The base interface for all services.</summary>
    [TypeId("::Ice::Object")]
    public interface IService : IDispatcher
    {
        // The following are helper classes and methods for generated servants.

        /// <summary>Provides static methods that read the arguments of requests.</summary>
        public static class Request
        {
            /// <summary>Reads the argument of operation ice_isA.</summary>
            /// <param name="payload">The payload of the incoming request.</param>
            /// <param name="dispatch">The dispatch properties.</param>
            /// <returns>The argument carried by the payload.</returns>
            public static string IceIsA(ReadOnlyMemory<byte> payload, Dispatch dispatch) =>
                payload.ToArgs(dispatch, BufferReader.IceReaderIntoString);
        }

        /// <summary>Provides static methods that create response payloads.</summary>
        public static class Response
        {
            /// <summary>Creates a response payload for operation ice_id.</summary>
            /// <param name="dispatch">The dispatch properties.</param>
            /// <param name="returnValue">The return value to write into the payload.</param>
            /// <returns>A new response payload.</returns>
            public static ReadOnlyMemory<ReadOnlyMemory<byte>> IceId(Dispatch dispatch, string returnValue) =>
                Payload.FromSingleReturnValue(dispatch, returnValue, BufferWriter.IceWriterFromString);

            /// <summary>Creates a response payload for operation ice_ids.</summary>
            /// <param name="dispatch">The dispatch properties.</param>
            /// <param name="returnValue">The return value to write into the payload.</param>
            /// <returns>A new response payload.</returns>
            public static ReadOnlyMemory<ReadOnlyMemory<byte>> IceIds(Dispatch dispatch, IEnumerable<string> returnValue) =>
                Payload.FromSingleReturnValue(
                    dispatch,
                    returnValue,
                    (writer, returnValue) => writer.WriteSequence(returnValue, BufferWriter.IceWriterFromString));

            /// <summary>Creates a response payload for operation ice_isA.</summary>
            /// <param name="dispatch">The dispatch properties.</param>
            /// <param name="returnValue">The return value to write into the payload.</param>
            /// <returns>A new response payload.</returns>
            public static ReadOnlyMemory<ReadOnlyMemory<byte>> IceIsA(Dispatch dispatch, bool returnValue) =>
                Payload.FromSingleReturnValue(dispatch, returnValue, BufferWriter.IceWriterFromBool);
        }

        /// <summary>Dispatches an incoming request and returns the corresponding response.</summary>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch properties, which include properties of both the request and response.
        /// </param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The response payload and optional stream encoder.</returns>
        public ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> DispatchAsync(
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel);

        /// <summary>Returns the Slice type ID of the most-derived interface supported by this object.</summary>
        /// <param name="dispatch">The dispatch properties.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is cancelled.
        /// </param>
        /// <returns>The Slice type ID of the most-derived interface.</returns>
        public ValueTask<string> IceIdAsync(Dispatch dispatch, CancellationToken cancel) => new("::Ice::Object");

        /// <summary>Returns the Slice type IDs of the interfaces supported by this object.</summary>
        /// <param name="dispatch">The dispatch properties.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The Slice type IDs of the interfaces supported by this object, in alphabetical order.</returns>
        public ValueTask<IEnumerable<string>> IceIdsAsync(Dispatch dispatch, CancellationToken cancel) =>
            new(new string[] { "::Ice::Object" });

        /// <summary>Tests whether this service supports the specified Slice interface.</summary>
        /// <param name="typeId">The type ID of the Slice interface to test against.</param>
        /// <param name="dispatch">The dispatch properties.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>True if this object implements the interface specified by typeId.</returns>
        public async ValueTask<bool> IceIsAAsync(string typeId, Dispatch dispatch, CancellationToken cancel)
        {
            var array = (string[])await IceIdsAsync(dispatch, cancel).ConfigureAwait(false);
            return Array.BinarySearch(array, typeId, StringComparer.Ordinal) >= 0;
        }

        /// <summary>Tests whether this object can be reached.</summary>
        /// <param name="dispatch">The dispatch properties.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        public ValueTask IcePingAsync(Dispatch dispatch, CancellationToken cancel) => default;

        /// <summary>The generated code calls this method to ensure that when an operation is _not_ declared
        /// idempotent, the request is not marked idempotent. If the request is marked idempotent, it means the caller
        /// incorrectly believes this operation is idempotent.</summary>
        /// <param name="dispatch">The dispatch.</param>
        protected static void IceCheckNonIdempotent(Dispatch dispatch)
        {
            if (dispatch.IsIdempotent)
            {
                throw new InvalidDataException(
                        $@"idempotent mismatch for operation '{dispatch.Operation
                        }': received request marked idempotent for a non-idempotent operation");
            }
        }

        /// <summary>The generated code calls this method to ensure that streaming is aborted if the operation
        /// doesn't specify a stream parameter.</summary>
        /// <param name="dispatch">The dispatch properties.</param>
        protected static void IceStreamReadingComplete(Dispatch dispatch) =>
            dispatch.IncomingRequest.Stream.AbortRead(Transports.RpcStreamError.UnexpectedStreamData);

        /// <summary>Dispatches an ice_id request.</summary>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        protected async ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> IceDIceIdAsync(
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            IceStreamReadingComplete(dispatch);
            payload.CheckEmptyArgs(dispatch);
            string returnValue = await IceIdAsync(dispatch, cancel).ConfigureAwait(false);
            return (Response.IceId(dispatch, returnValue), null);
        }

        /// <summary>Dispatches an ice_ids request.</summary>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        protected async ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> IceDIceIdsAsync(
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            IceStreamReadingComplete(dispatch);
            payload.CheckEmptyArgs(dispatch);
            IEnumerable<string> returnValue = await IceIdsAsync(dispatch, cancel).ConfigureAwait(false);
            return (Response.IceIds(dispatch, returnValue), null);
        }

        /// <summary>Dispatches an ice_isA request.</summary>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        protected async ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> IceDIceIsAAsync(
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            IceStreamReadingComplete(dispatch);
            string id = Request.IceIsA(payload, dispatch);
            bool returnValue = await IceIsAAsync(id, dispatch, cancel).ConfigureAwait(false);
            return (Response.IceIsA(dispatch, returnValue), null);
        }

        /// <summary>Dispatches an ice_ping request.</summary>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        protected async ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> IceDIcePingAsync(
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            IceStreamReadingComplete(dispatch);
            payload.CheckEmptyArgs(dispatch);
            await IcePingAsync(dispatch, cancel).ConfigureAwait(false);
            return (Payload.FromVoidReturnValue(dispatch), null);
        }

        async ValueTask<OutgoingResponse> IDispatcher.DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            var dispatch = new Dispatch(request);
            try
            {
                ReadOnlyMemory<byte> requestPayload = await request.GetPayloadAsync(cancel).ConfigureAwait(false);
                (ReadOnlyMemory<ReadOnlyMemory<byte>> responsePayload, RpcStreamWriter? streamWriter) =
                    await DispatchAsync(requestPayload, dispatch, cancel).ConfigureAwait(false);

                return new OutgoingResponse(dispatch, responsePayload, streamWriter);
            }
            catch (RemoteException exception)
            {
                exception.Features = dispatch.ResponseFeatures;
                throw;
            }
        }
    }
}
