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
            DefaultIceDecoderFactories defaultIceDecoderFactories,
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

            var decoder = GetIceDecoderFactory(dispatch.Encoding, dispatch.RequestFeatures, defaultIceDecoderFactories).
                CreateIceDecoder(payload, dispatch.Connection, dispatch.ProxyInvoker);

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

        internal static IIceDecoderFactory<IceDecoder> GetIceDecoderFactory(
            Encoding encoding,
            FeatureCollection features,
            DefaultIceDecoderFactories defaultIceDecoderFactories) =>
            encoding.Name switch
            {
                Encoding.Ice11Name => features.Get<IIceDecoderFactory<Ice11Decoder>>() ??
                    defaultIceDecoderFactories.Ice11DecoderFactory,
                Encoding.Ice20Name => features.Get<IIceDecoderFactory<Ice20Decoder>>() ??
                    defaultIceDecoderFactories.Ice20DecoderFactory,
                _ => throw new NotSupportedException($"cannot create Ice decoder for encoding {encoding}")
            };

        /// <summary>Creates a response payload from a <see cref="RemoteException"/>.</summary>
        /// <param name="request">The incoming request used to create this response payload. </param>
        /// <param name="exception">The exception.</param>
        /// <returns>A response payload containing the exception.</returns>
        internal static (ReadOnlyMemory<ReadOnlyMemory<byte>> Payload, ReplyStatus ReplyStatus) FromRemoteException(
            IncomingRequest request,
            RemoteException exception)
        {
            exception.Origin = new RemoteExceptionOrigin(request.Path, request.Operation);

            ReplyStatus replyStatus = ReplyStatus.UserException;
            if (request.PayloadEncoding == Encoding.Ice11)
            {
                replyStatus = exception switch
                {
                    ServiceNotFoundException _ => ReplyStatus.ObjectNotExistException,
                    OperationNotFoundException _ => ReplyStatus.OperationNotExistException,
                    UnhandledException _ => ReplyStatus.UnknownLocalException,
                    _ => ReplyStatus.UserException
                };
            }

            var bufferWriter = new BufferWriter();

            if (request.Protocol == Protocol.Ice2 || replyStatus == ReplyStatus.UserException)
            {
                var encoder = request.PayloadEncoding.CreateIceEncoder(bufferWriter, classFormat: FormatType.Sliced);

                if (request.Protocol == Protocol.Ice2 && request.PayloadEncoding == Encoding.Ice11)
                {
                    // The first byte of the payload is the actual ReplyStatus in this case.
                    encoder.EncodeReplyStatus(replyStatus);

                    if (replyStatus == ReplyStatus.UserException)
                    {
                        encoder.EncodeException(exception);
                    }
                    else
                    {
                        encoder.EncodeIce1SystemException(replyStatus, request, exception.Message);
                    }
                }
                else
                {
                    encoder.EncodeException(exception);
                }
            }
            else
            {
                Debug.Assert(request.Protocol == Protocol.Ice1 && replyStatus > ReplyStatus.UserException);
                var encoder = new Ice11Encoder(bufferWriter);
                encoder.EncodeIce1SystemException(replyStatus, request, exception.Message);
            }

            return (bufferWriter.Finish(), replyStatus);
        }

        /// <summary>Reads a remote exception from a response payload.</summary>
        /// <param name="payload">The response's payload.</param>
        /// <param name="payloadEncoding">The response's payload encoding.</param>
        /// <param name="replyStatus">The reply status.</param>
        /// <param name="connection">The connection that received this response.</param>
        /// <param name="invoker">The invoker of the proxy that sent the request.</param>
        /// <returns>The remote exception.</returns>
        internal static RemoteException ToRemoteException(
            this ReadOnlyMemory<byte> payload,
            Encoding payloadEncoding,
            ReplyStatus replyStatus,
            Connection connection,
            IInvoker? invoker)
        {
            if (payload.Length == 0 || replyStatus == ReplyStatus.OK)
            {
                throw new ArgumentException("payload does not carry a remote exception", nameof(payload));
            }

            Protocol protocol = connection.Protocol;
            var decoder = payloadEncoding.CreateIceDecoder(payload, connection, invoker);

            if (protocol == Protocol.Ice2 && payloadEncoding == Encoding.Ice11)
            {
                // Skip reply status byte
                decoder.Skip(1);
            }

            RemoteException exception;
            if (payloadEncoding == Encoding.Ice11 && replyStatus != ReplyStatus.UserException)
            {
                exception = ((Ice11Decoder)decoder).DecodeIce1SystemException(replyStatus);
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
            }
            else
            {
                exception = decoder.DecodeException();
                decoder.CheckEndOfBuffer(skipTaggedParams: false);
            }
            return exception;
        }
    }
}
