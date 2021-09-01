// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>A function that decodes the return value from an Ice-encoded response.</summary>
    /// <typeparam name="T">The type of the return value to read.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="invoker">The invoker of the proxy used to send this request.</param>
    /// <param name="streamParamReceiver">The stream param receiver from the response.</param>
    /// <returns>The response return value.</returns>
    /// <exception cref="RemoteException">Thrown when the response payload carries a failure.</exception>
    public delegate T ResponseDecodeFunc<T>(
        IncomingResponse response,
        IInvoker? invoker,
        StreamParamReceiver? streamParamReceiver);

    /// <summary>Provides extension methods for class Proxy.</summary>
    public static class ProxyExtensions
    {
        /// <summary>Computes the Ice encoding to use when encoding a Slice-generated request.</summary>
        public static IceEncoding GetIceEncoding(this Proxy proxy) =>
            proxy.Encoding as IceEncoding ?? proxy.Protocol.GetIceEncoding() ??
                throw new NotSupportedException($"unknown protocol {proxy.Protocol.GetName()}");

        /// <summary>Sends a request to a service and decodes the response.</summary>
        /// <param name="proxy">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="payloadEncoding">The encoding of the request payload.</param>
        /// <param name="requestPayload">The payload of the request.</param>
        /// <param name="streamParamSender">The stream param sender.</param>
        /// <param name="responseDecodeFunc">The decode function for the response payload. It decodes and throws a
        /// <see cref="RemoteException"/> when the response payload contains a failure.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="compress">When <c>true</c>, the request payload should be compressed.</param>
        /// <param name="idempotent">When <c>true</c>, the request is idempotent.</param>
        /// <param name="returnStreamParamReceiver"><c>true</c> if the response has a stream value.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The operation's return value.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when
        /// invocation is not null.</remarks>
        public static Task<T> InvokeAsync<T>(
            this Proxy proxy,
            string operation,
            IceEncoding payloadEncoding,
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload,
            IStreamParamSender? streamParamSender,
            ResponseDecodeFunc<T> responseDecodeFunc,
            Invocation? invocation,
            bool compress = false,
            bool idempotent = false,
            bool returnStreamParamReceiver = false,
            CancellationToken cancel = default)
        {
            Task<(IncomingResponse, StreamParamReceiver?)> responseTask =
                proxy.InvokeAsync(
                    operation,
                    payloadEncoding,
                    requestPayload,
                    streamParamSender,
                    invocation,
                    compress,
                    idempotent,
                    oneway: false,
                    returnStreamParamReceiver: returnStreamParamReceiver,
                    cancel);

            return ReadResponseAsync();

            async Task<T> ReadResponseAsync()
            {
                (IncomingResponse response, StreamParamReceiver? streamParamReceiver) =
                    await responseTask.ConfigureAwait(false);

                return responseDecodeFunc(response, proxy.Invoker, streamParamReceiver);
            }
        }

        /// <summary>Sends a request to a service and decodes the "void" response.</summary>
        /// <param name="proxy">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="payloadEncoding">The encoding of the request payload.</param>
        /// <param name="requestPayload">The payload of the request.</param>
        /// <param name="defaultIceDecoderFactories">The default Ice decoder factories.</param>
        /// <param name="streamParamSender">The stream param sender.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="compress">When true, the request payload should be compressed.</param>
        /// <param name="idempotent">When true, the request is idempotent.</param>
        /// <param name="oneway">When true, the request is sent oneway and an empty response is returned immediately
        /// after sending the request.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A task that completes when the void response is returned.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when invocation is
        /// not null.</remarks>
        public static Task InvokeAsync(
            this Proxy proxy,
            string operation,
            IceEncoding payloadEncoding,
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload,
            DefaultIceDecoderFactories defaultIceDecoderFactories,
            IStreamParamSender? streamParamSender,
            Invocation? invocation,
            bool compress = false,
            bool idempotent = false,
            bool oneway = false,
            CancellationToken cancel = default)
        {
            Task<(IncomingResponse, StreamParamReceiver?)> responseTask =
                proxy.InvokeAsync(
                    operation,
                    payloadEncoding,
                    requestPayload,
                    streamParamSender,
                    invocation,
                    compress,
                    idempotent,
                    oneway,
                    returnStreamParamReceiver: false,
                    cancel);

            return ReadResponseAsync();

            async Task ReadResponseAsync()
            {
                (IncomingResponse response, StreamParamReceiver? _) = await responseTask.ConfigureAwait(false);

                response.CheckVoidReturnValue(proxy.Invoker, defaultIceDecoderFactories);
            }
        }
    }
}
