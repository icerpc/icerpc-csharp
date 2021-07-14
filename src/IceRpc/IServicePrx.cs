// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>A function that decodes the return value from an Ice-encoded response payload.</summary>
    /// <typeparam name="T">The type of the return value to read.</typeparam>
    /// <param name="payload">The response payload.</param>
    /// <param name="streamReader">The stream reader from the response.</param>
    /// <param name="payloadEncoding">The encoding of the response payload.</param>
    /// <param name="connection">The connection that received this response.</param>
    /// <param name="invoker">The invoker of the proxy used to send this request.</param>
    /// <returns>The response return value.</returns>
    /// <exception cref="RemoteException">Thrown when the response payload carries a failure.</exception>
    public delegate T ResponseDecodeFunc<T>(
        ReadOnlyMemory<byte> payload,
        RpcStreamReader? streamReader,
        Encoding payloadEncoding,
        Connection connection,
        IInvoker? invoker);

    public partial interface IServicePrx
    {
        /// <summary>Sends a request to a service and decodes the response.</summary>
        /// <param name="proxy">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="requestPayload">The payload of the request.</param>
        /// <param name="streamWriter">The stream writer to write the stream parameter on the
        /// <see cref="Transports.RpcStream"/>.</param>
        /// <param name="responseDecodeFunc">The decode function for the response payload. It decodes and throws a
        /// <see cref="RemoteException"/> when the response payload contains a failure.</param>
        /// <param name="invocation">The invocation properties.</param>
        /// <param name="compress">When <c>true</c>, the request payload should be compressed.</param>
        /// <param name="idempotent">When <c>true</c>, the request is idempotent.</param>
        /// <param name="responseHasStreamValue"><c>true</c> if the response has a stream value.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The operation's return value.</returns>
        /// <exception cref="RemoteException">Thrown if the response carries a failure.</exception>
        /// <remarks>This method stores the response features into the invocation's response features when
        /// invocation is not null.</remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected static Task<T> IceInvokeAsync<T>(
            Proxy proxy,
            string operation,
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload,
            RpcStreamWriter? streamWriter,
            ResponseDecodeFunc<T> responseDecodeFunc,
            Invocation? invocation,
            bool compress = false,
            bool idempotent = false,
            bool responseHasStreamValue = false,
            CancellationToken cancel = default)
        {
            Task<(ReadOnlyMemory<byte>, RpcStreamReader?, Encoding, Connection)> responseTask =
                ProxyExtensions.InvokeAsync(
                    proxy,
                    operation,
                    requestPayload,
                    streamWriter,
                    invocation,
                    compress,
                    idempotent,
                    oneway: false,
                    returnStreamReader: responseHasStreamValue,
                    cancel);

            return ReadResponseAsync();

            async Task<T> ReadResponseAsync()
            {
                (ReadOnlyMemory<byte> payload, RpcStreamReader? streamReader, Encoding payloadEncoding, Connection connection) =
                    await responseTask.ConfigureAwait(false);

                return responseDecodeFunc(payload, streamReader, payloadEncoding, connection, Invoker);
            }
        }

        /// <summary>Sends a request to a service and decodes the "void" response.</summary>
        /// <param name="proxy">A proxy for the remote service.</param>
        /// <param name="operation">The name of the operation, as specified in Slice.</param>
        /// <param name="requestPayload">The payload of the request.</param>
        /// <param name="streamWriter">The stream writer to write the stream parameter on the
        /// <see cref="Transports.RpcStream"/>.</param>
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
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected static Task IceInvokeAsync(
            Proxy proxy,
            string operation,
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload,
            RpcStreamWriter? streamWriter,
            Invocation? invocation,
            bool compress = false,
            bool idempotent = false,
            bool oneway = false,
            CancellationToken cancel = default)
        {
            Task<(ReadOnlyMemory<byte>, RpcStreamReader?, Encoding, Connection)> responseTask = this.InvokeAsync(
                operation,
                requestPayload,
                streamWriter,
                invocation,
                compress,
                idempotent,
                oneway,
                returnStreamReader: false,
                cancel);

            return ReadResponseAsync();

            async Task ReadResponseAsync()
            {
                (ReadOnlyMemory<byte> payload, RpcStreamReader? _, Encoding payloadEncoding, _) =
                    await responseTask.ConfigureAwait(false);

                payload.CheckVoidReturnValue(payloadEncoding);
            }
        }
    }
}
