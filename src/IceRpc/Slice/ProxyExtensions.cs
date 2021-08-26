// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

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
        /// <summary>Creates the payload of a request without parameter.</summary>
        /// <param name="proxy">A proxy to the target service.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateEmptyPayload(this Proxy proxy)
        {
            proxy.Encoding.CheckSupportedIceEncoding();
            return default;
        }

        /// <summary>Creates the payload of a request from the request's argument. Use this method when the operation
        /// takes a single parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="proxy">A proxy to the target service.</param>
        /// <param name="arg">The argument to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{T}"/> that encodes the argument into the payload.
        /// </param>
        /// <param name="classFormat">The class format in case T is a class.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromSingleArg<T>(
            this Proxy proxy,
            T arg,
            EncodeAction<T> encodeAction,
            FormatType classFormat = default)
        {
            var bufferWriter = new BufferWriter();
            var encoder = proxy.Encoding.CreateIceEncoder(bufferWriter, classFormat: classFormat);
            encodeAction(encoder, arg);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a request from the request's arguments. Use this method is for operations
        /// with multiple parameters.</summary>
        /// <typeparam name="T">The type of the operation's parameters.</typeparam>
        /// <param name="proxy">A proxy to the target service.</param>
        /// <param name="args">The arguments to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{T}"/> that encodes the arguments into the
        /// payload.</param>
        /// <param name="classFormat">The class format in case any parameter is a class.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromArgs<T>(
            this Proxy proxy,
            in T args,
            TupleEncodeAction<T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var bufferWriter = new BufferWriter();
            var encoder = proxy.Encoding.CreateIceEncoder(bufferWriter, classFormat: classFormat);
            encodeAction(encoder, in args);
            return bufferWriter.Finish();
        }

        /// <summary>Sends a request to a service and decodes the response.</summary>
        /// <param name="proxy">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
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
