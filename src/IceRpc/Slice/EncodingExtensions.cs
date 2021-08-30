// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>Provides extension methods for class Encoding.</summary>
    public static class EncodingExtensions
    {
        /// <summary>Creates the payload of a response from the request's dispatch and return value tuple. Use this
        /// method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's return value tuple.</typeparam>
        /// <param name="payloadEncoding">The payload encoding.</param>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{TEncoder, T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload encoded with the Ice 1.1 encoding.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateIce11PayloadFromReturnValueTuple<T>(
            this Encoding payloadEncoding,
            in T returnValueTuple,
            TupleEncodeAction<Ice11Encoder, T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var bufferWriter = new BufferWriter();
            var encoder = new Ice11Encoder(bufferWriter, classFormat);
            encodeAction(encoder, in returnValueTuple);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value. Use this method
        /// when the operation returns a single value.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="payloadEncoding">The payload encoding.</param>
        /// <param name="returnValue">The return value to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{TEncoder, T}"/> that encodes the argument into the
        /// payload.</param>
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload with the Ice 1.1 encoding.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreateIce11PayloadFromSingleReturnValue<T>(
            this Encoding payloadEncoding,
            T returnValue,
            EncodeAction<Ice11Encoder, T> encodeAction,
            FormatType classFormat = default)
        {
            var bufferWriter = new BufferWriter();
            var encoder = new Ice11Encoder(bufferWriter, classFormat);
            encodeAction(encoder, returnValue);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value tuple. Use this
        /// method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's return value tuple.</typeparam>
        /// <param name="payloadEncoding">The payload encoding.</param>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{TEncoder, T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromReturnValueTuple<T>(
            this Encoding payloadEncoding,
            in T returnValueTuple,
            TupleEncodeAction<IceEncoder, T> encodeAction) where T : struct
        {
            var bufferWriter = new BufferWriter();
            var encoder = payloadEncoding.CreateIceEncoder(bufferWriter);
            encodeAction(encoder, in returnValueTuple);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value. Use this method
        /// when the operation returns a single value.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="payloadEncoding">The payload encoding.</param>
        /// <param name="returnValue">The return value to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{TEncoder, T}"/> that encodes the argument into the
        /// payload.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromSingleReturnValue<T>(
            this Encoding payloadEncoding,
            T returnValue,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            var bufferWriter = new BufferWriter();
            var encoder = payloadEncoding.CreateIceEncoder(bufferWriter);
            encodeAction(encoder, returnValue);
            return bufferWriter.Finish();
        }

        /// <summary>Creates a payload representing a void return value.</summary>
        /// <param name="payloadEncoding">The payload encoding.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromVoidReturnValue(
            this Encoding payloadEncoding)
        {
            payloadEncoding.CheckSupportedIceEncoding();
            return default;
        }
    }
}
