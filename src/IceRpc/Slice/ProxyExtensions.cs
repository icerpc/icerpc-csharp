// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>A function that decodes the return value from an Ice-encoded response.</summary>
    /// <typeparam name="T">The type of the return value to read.</typeparam>
    /// <param name="response">The incoming response.</param>
    /// <param name="invoker">The invoker of the proxy used to send this request.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>A value task that contains the return value or a <see cref="RemoteException"/> when the response
    /// carries a failure.</returns>
    public delegate ValueTask<T> ResponseDecodeFunc<T>(
        IncomingResponse response,
        IInvoker? invoker,
        CancellationToken cancel);

    /// <summary>Provides extension methods for class Proxy.</summary>
    public static class ProxyExtensions
    {
        /// <summary>Computes the Ice encoding to use when encoding a Slice-generated request.</summary>
        public static IceEncoding GetIceEncoding(this Proxy proxy) =>
            proxy.Encoding as IceEncoding ?? proxy.Protocol.IceEncoding ??
                throw new NotSupportedException($"unknown protocol {proxy.Protocol}");

        /// <summary>Sends a request to a service and decodes the response.</summary>
        /// <param name="proxy">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="payloadEncoding">The encoding of the request payload.</param>
        /// <param name="payloadSource">The payload source of the request.</param>
        /// <param name="payloadSourceStream">The optional payload source stream of the request.</param>
        /// <param name="responseDecodeFunc">The decode function for the response payload. It decodes and throws a
        /// <see cref="RemoteException"/> when the response payload contains a failure.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="idempotent">When <c>true</c>, the request is idempotent.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The operation's return value.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when
        /// invocation is not null.</remarks>
        public static Task<T> InvokeAsync<T>(
            this Proxy proxy,
            string operation,
            IceEncoding payloadEncoding,
            PipeReader payloadSource,
            PipeReader? payloadSourceStream,
            ResponseDecodeFunc<T> responseDecodeFunc,
            Invocation? invocation,
            bool idempotent = false,
            CancellationToken cancel = default)
        {
            Task<IncomingResponse> responseTask =
                proxy.InvokeAsync(
                    operation,
                    payloadEncoding,
                    payloadSource,
                    payloadSourceStream,
                    invocation,
                    idempotent,
                    oneway: false,
                    cancel);

            return ReadResponseAsync();

            async Task<T> ReadResponseAsync()
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                return await responseDecodeFunc(
                    response,
                    proxy.Invoker,
                    cancel).ConfigureAwait(false);
            }
        }

        /// <summary>Sends a request to a service and decodes the "void" response.</summary>
        /// <param name="proxy">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="payloadEncoding">The encoding of the request payload.</param>
        /// <param name="payloadSource">The payload source of the request.</param>
        /// <param name="payloadSourceStream">The payload source stream of the request.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="invocation">The invocation properties.</param>
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
            PipeReader payloadSource,
            PipeReader? payloadSourceStream,
            IActivator defaultActivator,
            Invocation? invocation,
            bool idempotent = false,
            bool oneway = false,
            CancellationToken cancel = default)
        {
            Task<IncomingResponse> responseTask =
                proxy.InvokeAsync(
                    operation,
                    payloadEncoding,
                    payloadSource,
                    payloadSourceStream,
                    invocation,
                    idempotent,
                    oneway,
                    cancel);

            return ReadResponseAsync();

            async Task ReadResponseAsync()
            {
                IncomingResponse response = await responseTask.ConfigureAwait(false);

                await response.CheckVoidReturnValueAsync(
                    proxy.Invoker,
                    defaultActivator,
                    cancel).ConfigureAwait(false);
            }
        }
    }
}
