// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payloads of incoming responses when such payloads are encoded with the
    /// Ice encoding.</summary>
    public static class IncomingResponseExtensions
    {
        /// <summary>Decodes a response when the corresponding operation returns void.</summary>
        /// <param name="response">The incoming response.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="cancel">The cancellation token.</param>
        public static async ValueTask CheckVoidReturnValueAsync(
            this IncomingResponse response,
            IActivator defaultActivator,
            CancellationToken cancel)
        {
            if (response.ResultType == ResultType.Success)
            {
                await response.Payload.ReadVoidAsync(response.GetSlicePayloadEncoding(), cancel).ConfigureAwait(false);
            }
            else
            {
                throw await response.Payload.ReadRemoteExceptionAsync(
                    response.GetSlicePayloadEncoding(),
                    response.Connection,
                    response.ProxyInvoker,
                    response.Features.Get<IActivator>() ?? defaultActivator,
                    response.Features.GetClassGraphMaxDepth(),
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>Decodes a response payload.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="response">The incoming response.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="hasStream">When true, T is or includes a stream return.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The return value.</returns>
        public static async ValueTask<T> ToReturnValueAsync<T>(
            this IncomingResponse response,
            IActivator defaultActivator,
            DecodeFunc<T> decodeFunc,
            bool hasStream,
            CancellationToken cancel) =>
            response.ResultType == ResultType.Success ?
                await response.Payload.ReadValueAsync(
                    response.GetSlicePayloadEncoding(),
                    response.Connection,
                    response.ProxyInvoker,
                    response.Features.Get<IActivator>() ?? defaultActivator,
                    response.Features.GetClassGraphMaxDepth(),
                    decodeFunc,
                    hasStream,
                    cancel).ConfigureAwait(false) :
                throw await response.Payload.ReadRemoteExceptionAsync(
                    response.GetSlicePayloadEncoding(),
                    response.Connection,
                    response.ProxyInvoker,
                    response.Features.Get<IActivator>() ?? defaultActivator,
                    response.Features.GetClassGraphMaxDepth(),
                    cancel).ConfigureAwait(false);
    }
}
