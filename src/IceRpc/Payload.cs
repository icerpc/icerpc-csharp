// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Methods to read and write the payloads of requests and responses.</summary>
    public static class Payload
    {
        /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch properties.</param>
        public static void CheckEmptyArgs(this ReadOnlyMemory<byte> payload, Dispatch dispatch)
        {
            if (dispatch.Encoding == Encoding.Ice20)
            {
                if (payload.Length == 0)
                {
                    throw new ArgumentException("invalid empty payload", nameof(payload));
                }
                if ((CompressionFormat)payload.Span[0] != CompressionFormat.NotCompressed)
                {
                    throw new ArgumentException("cannot read compressed payload");
                }
                payload = payload[1..];
            }
            dispatch.Encoding.CreateIceDecoder(payload).CheckEndOfBuffer(skipTaggedParams: true);
        }

        /// <summary>Reads a response payload and ensures it carries a void return value.</summary>
        /// <param name="payload">The response payload.</param>
        /// <param name="payloadEncoding">The response's payload encoding.</param>
        public static void CheckVoidReturnValue(this ReadOnlyMemory<byte> payload, Encoding payloadEncoding)
        {
            if (payloadEncoding == Encoding.Ice20)
            {
                if (payload.Length == 0)
                {
                    throw new ArgumentException("invalid empty payload", nameof(payload));
                }
                if ((CompressionFormat)payload.Span[0] != CompressionFormat.NotCompressed)
                {
                    throw new ArgumentException("cannot read compressed payload");
                }
                payload = payload[1..];
            }

            payloadEncoding.CreateIceDecoder(payload).CheckEndOfBuffer(skipTaggedParams: true);
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
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> FromArgs<T>(
            Proxy proxy,
            in T args,
            TupleEncodeAction<T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var bufferWriter = new BufferWriter();
            var encoder = proxy.Encoding.CreateIceEncoder(bufferWriter, classFormat: classFormat);
            if (proxy.Encoding == Encoding.Ice20)
            {
                encoder.EncodeCompressionFormat(CompressionFormat.NotCompressed);
            }

            encodeAction(encoder, in args);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a request without parameter.</summary>
        /// <param name="proxy">A proxy to the target service.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> FromEmptyArgs(Proxy proxy) =>
            new ReadOnlyMemory<byte>[] { proxy.Protocol.GetEmptyArgsPayload(proxy.Encoding) };

        /// <summary>Creates the payload of a response from the request's dispatch and return value tuple. Use this
        /// method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's return value tuple.</typeparam>
        /// <param name="payloadEncoding">The payload encoding.</param>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{T}"/> that encodes the arguments into the
        /// payload.</param>
        /// <param name="classFormat">The class format in case T is a class.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> FromReturnValueTuple<T>(
            Encoding payloadEncoding,
            in T returnValueTuple,
            TupleEncodeAction<T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var bufferWriter = new BufferWriter();
            var encoder = payloadEncoding.CreateIceEncoder(bufferWriter, classFormat: classFormat);
            if (payloadEncoding == Encoding.Ice20)
            {
                encoder.EncodeCompressionFormat(CompressionFormat.NotCompressed);
            }

            encodeAction(encoder, in returnValueTuple);
            return bufferWriter.Finish();
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
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> FromSingleArg<T>(
            Proxy proxy,
            T arg,
            EncodeAction<T> encodeAction,
            FormatType classFormat = default)
        {
            var bufferWriter = new BufferWriter();
            var encoder = proxy.Encoding.CreateIceEncoder(bufferWriter, classFormat: classFormat);
            if (proxy.Encoding == Encoding.Ice20)
            {
                encoder.EncodeCompressionFormat(CompressionFormat.NotCompressed);
            }

            encodeAction(encoder, arg);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value. Use this method
        /// when the operation returns a single value.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="payloadEncoding">The payload encoding.</param>
        /// <param name="returnValue">The return value to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{T}"/> that encodes the argument into the payload.
        /// </param>
        /// <param name="classFormat">The class format in case T is a class.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> FromSingleReturnValue<T>(
            Encoding payloadEncoding,
            T returnValue,
            EncodeAction<T> encodeAction,
            FormatType classFormat = default)
        {
            var bufferWriter = new BufferWriter();
            var encoder = payloadEncoding.CreateIceEncoder(bufferWriter, classFormat: classFormat);
            if (payloadEncoding == Encoding.Ice20)
            {
                encoder.EncodeCompressionFormat(CompressionFormat.NotCompressed);
            }

            encodeAction(encoder, returnValue);
            return bufferWriter.Finish();
        }

        /// <summary>Creates a payload representing a void return value.</summary>
        /// <param name="dispatch">The request's dispatch properties.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> FromVoidReturnValue(Dispatch dispatch) =>
            FromVoidReturnValue(dispatch.IncomingRequest);

        /// <summary>Creates a payload representing a void return value.</summary>
        /// <param name="request">The request.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> FromVoidReturnValue(IncomingRequest request) =>
            new ReadOnlyMemory<byte>[] { request.Protocol.GetVoidReturnPayload(request.PayloadEncoding) };

        /// <summary>Reads a request payload and decodes this payload into a list of arguments.</summary>
        /// <paramtype name="T">The type of the request parameters.</paramtype>
        /// <param name="payload">The request payload.</param>
        /// <param name="dispatch">The dispatch properties.</param>
        /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
        /// <returns>The request arguments.</returns>
        public static T ToArgs<T>(
            this ReadOnlyMemory<byte> payload,
            Dispatch dispatch,
            DecodeFunc<T> decodeFunc)
        {
            if (dispatch.Encoding == Encoding.Ice20)
            {
                if (payload.Length == 0)
                {
                    throw new ArgumentException("invalid empty payload", nameof(payload));
                }

                if ((CompressionFormat)payload.Span[0] != CompressionFormat.NotCompressed)
                {
                    throw new ArgumentException("cannot read compressed payload");
                }
                payload = payload[1..];
            }

            var decoder = dispatch.Encoding.CreateIceDecoder(payload, dispatch.Connection, dispatch.ProxyInvoker);
            T result = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: true);
            return result;
        }

        /// <summary>Reads a response payload and decodes this payload into a return value.</summary>
        /// <paramtype name="T">The type of the return value.</paramtype>
        /// <param name="payload">The response payload.</param>
        /// <param name="payloadEncoding">The response's payload encoding.</param>
        /// <param name="decodeFunc">The decode function for the return value.</param>
        /// <param name="connection">The connection that received this response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <returns>The return value.</returns>
        public static T ToReturnValue<T>(
            this ReadOnlyMemory<byte> payload,
            Encoding payloadEncoding,
            DecodeFunc<T> decodeFunc,
            Connection connection,
            IInvoker? invoker)
        {
            if (payloadEncoding == Encoding.Ice20)
            {
                if (payload.Length == 0)
                {
                    throw new ArgumentException("invalid empty payload", nameof(payload));
                }

                if ((CompressionFormat)payload.Span[0] != CompressionFormat.NotCompressed)
                {
                    throw new ArgumentException("cannot read compressed payload");
                }
                payload = payload[1..];
            }

            var decoder = payloadEncoding.CreateIceDecoder(payload, connection, invoker);
            T result = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: true);
            return result;
        }
    }
}
