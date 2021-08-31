// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>Provides extension methods for class Encoding.</summary>
    public static class EncodingExtensions
    {
        /// <summary>Creates the payload of a request from the request's argument. Use this method when the operation
        /// takes a single parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="payloadEncoding">The payload encoding.</param>
        /// <param name="arg">The argument to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{TEncoder, T}"/> that encodes the argument into the
        /// payload.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromSingleArg<T>(
            this Encoding payloadEncoding,
            T arg,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            var bufferWriter = new BufferWriter();
            var encoder = payloadEncoding.CreateIceEncoder(bufferWriter);
            encodeAction(encoder, arg);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a request from the request's arguments. Use this method is for operations
        /// with multiple parameters.</summary>
        /// <typeparam name="T">The type of the operation's parameters.</typeparam>
        /// <param name="payloadEncoding">The payload encoding.</param>
        /// <param name="args">The arguments to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{TEncoder, T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <returns>A new payload.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromArgs<T>(
            this Encoding payloadEncoding,
            in T args,
            TupleEncodeAction<IceEncoder, T> encodeAction) where T : struct
        {
            var bufferWriter = new BufferWriter();
            var encoder = payloadEncoding.CreateIceEncoder(bufferWriter);
            encodeAction(encoder, in args);
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
            IceEncoder encoder = payloadEncoding.CreateIceEncoder(bufferWriter);
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
            IceEncoder encoder = payloadEncoding.CreateIceEncoder(bufferWriter);
            encodeAction(encoder, returnValue);
            return bufferWriter.Finish();
        }
    }
}
