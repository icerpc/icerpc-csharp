// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The base interface for all services.</summary>
    [TypeId("::Ice::Object")]
    public interface IService
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
                payload.ToArgs(dispatch, BasicDecodeFuncs.StringDecodeFunc);
        }

        /// <summary>Provides static methods that create response payloads.</summary>
        public static class Response
        {
            /// <summary>Creates a response payload for operation ice_id.</summary>
            /// <param name="dispatch">The dispatch properties.</param>
            /// <param name="returnValue">The return value to write into the payload.</param>
            /// <returns>A new response payload.</returns>
            public static ReadOnlyMemory<ReadOnlyMemory<byte>> IceId(Dispatch dispatch, string returnValue) =>
                Payload.FromSingleReturnValue(dispatch.Encoding, returnValue, BasicEncodeActions.StringEncodeAction);

            /// <summary>Creates a response payload for operation ice_ids.</summary>
            /// <param name="dispatch">The dispatch properties.</param>
            /// <param name="returnValue">The return value to write into the payload.</param>
            /// <returns>A new response payload.</returns>
            public static ReadOnlyMemory<ReadOnlyMemory<byte>> IceIds(Dispatch dispatch, IEnumerable<string> returnValue) =>
                Payload.FromSingleReturnValue(
                    dispatch.Encoding,
                    returnValue,
                    (encoder, returnValue) => encoder.EncodeSequence(returnValue, BasicEncodeActions.StringEncodeAction));

            /// <summary>Creates a response payload for operation ice_isA.</summary>
            /// <param name="dispatch">The dispatch properties.</param>
            /// <param name="returnValue">The return value to write into the payload.</param>
            /// <returns>A new response payload.</returns>
            public static ReadOnlyMemory<ReadOnlyMemory<byte>> IceIsA(Dispatch dispatch, bool returnValue) =>
                Payload.FromSingleReturnValue(dispatch.Encoding, returnValue, BasicEncodeActions.BoolEncodeAction);
        }

        /// <summary>Returns the Slice type IDs of the interfaces supported by this object.</summary>
        /// <param name="dispatch">The dispatch properties.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The Slice type IDs of the interfaces supported by this object, in alphabetical order.</returns>
        public ValueTask<IEnumerable<string>> IceIdsAsync(Dispatch dispatch, CancellationToken cancel);

        /// <summary>Tests whether this service supports the specified Slice interface.</summary>
        /// <param name="typeId">The type ID of the Slice interface to test against.</param>
        /// <param name="dispatch">The dispatch properties.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>True if this object implements the interface specified by typeId.</returns>
        public ValueTask<bool> IceIsAAsync(string typeId, Dispatch dispatch, CancellationToken cancel);

        /// <summary>Tests whether this object can be reached.</summary>
        /// <param name="dispatch">The dispatch properties.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        public ValueTask IcePingAsync(Dispatch dispatch, CancellationToken cancel);

        /// <summary>The generated code calls this method to ensure that when an operation is _not_ declared
        /// idempotent, the request is not marked idempotent. If the request is marked idempotent, it means the caller
        /// incorrectly believes this operation is idempotent.</summary>
        /// <param name="dispatch">The dispatch.</param>
        public static void IceCheckNonIdempotent(Dispatch dispatch)
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
        public static void IceStreamReadingComplete(Dispatch dispatch) =>
            dispatch.IncomingRequest.Stream.AbortRead(Transports.RpcStreamError.UnexpectedStreamData);

        /// <summary>Dispatches an ice_ids request.</summary>
        /// <param name="target">The target service.</param>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        [Operation("ice_ids")]
        protected static async ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> IceDIceIdsAsync(
            IService target,
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            IceStreamReadingComplete(dispatch);
            payload.CheckEmptyArgs(dispatch);
            IEnumerable<string> returnValue = await target.IceIdsAsync(dispatch, cancel).ConfigureAwait(false);
            return (Response.IceIds(dispatch, returnValue), null);
        }

        /// <summary>Dispatches an ice_isA request.</summary>
        /// <param name="target">The target service.</param>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        [Operation("ice_isA")]
        protected static async ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> IceDIceIsAAsync(
            IService target,
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            IceStreamReadingComplete(dispatch);
            string id = Request.IceIsA(payload, dispatch);
            bool returnValue = await target.IceIsAAsync(id, dispatch, cancel).ConfigureAwait(false);
            return (Response.IceIsA(dispatch, returnValue), null);
        }

        /// <summary>Dispatches an ice_ping request.</summary>
        /// <param name="target">The target service.</param>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch for this request.</param>
        /// <param name="cancel">A cancellation token that is notified of cancellation when the dispatch is canceled.
        /// </param>
        /// <returns>The response frame.</returns>
        [Operation("ice_ping")]
        protected static async ValueTask<(ReadOnlyMemory<ReadOnlyMemory<byte>>, RpcStreamWriter?)> IceDIcePingAsync(
            IService target,
            ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            CancellationToken cancel)
        {
            IceStreamReadingComplete(dispatch);
            payload.CheckEmptyArgs(dispatch);
            await target.IcePingAsync(dispatch, cancel).ConfigureAwait(false);
            return (Payload.FromVoidReturnValue(dispatch), null);
        }
    }
}
