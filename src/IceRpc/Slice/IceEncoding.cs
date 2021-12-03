// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;

namespace IceRpc.Slice
{
    /// <summary>The base class for Ice encodings supported by this IceRPC runtime.</summary>
    public abstract class IceEncoding : Encoding
    {
        /// <summary>Returns a supported Ice encoding with the given name.</summary>
        /// <param name="name">The name of the encoding.</param>
        /// <returns>A supported Ice encoding.</returns>
        public static new IceEncoding FromString(string name) =>
            name switch
            {
                Ice11Name => Ice11,
                Ice20Name => Ice20,
                _ => throw new ArgumentException($"{name} is not the name of a supported Ice encoding", nameof(name))
            };

        /// <summary>Creates an empty payload encoded with this encoding.</summary>
        /// <returns>A new empty payload.</returns>
        // TODO: the term payload is not quite correct here. For this class, it currently represents only the
        // args/return/exception portion of the payload; the actual payload of a request/response can also hold stream
        // data.
        public abstract ReadOnlyMemory<ReadOnlyMemory<byte>> CreateEmptyPayload();

        /// <summary>Creates the payload of a request from the request's argument. Use this method when the operation
        /// takes a single parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="arg">The argument to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{TEncoder, T}"/> that encodes the argument into the
        /// payload.</param>
        /// <returns>A new payload.</returns>
        public ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromSingleArg<T>(
            T arg,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            var bufferWriter = new BufferWriter();
            IceEncoder encoder = CreateIceEncoder(bufferWriter);
            BufferWriter.Position start = encoder.StartFixedLengthSize();
            encodeAction(encoder, arg);
            _ = encoder.EndFixedLengthSize(start);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a request from the request's arguments. Use this method is for operations
        /// with multiple parameters.</summary>
        /// <typeparam name="T">The type of the operation's parameters.</typeparam>
        /// <param name="args">The arguments to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{TEncoder, T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <returns>A new payload.</returns>
        public ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromArgs<T>(
            in T args,
            TupleEncodeAction<IceEncoder, T> encodeAction) where T : struct
        {
            var bufferWriter = new BufferWriter();
            IceEncoder encoder = CreateIceEncoder(bufferWriter);
            BufferWriter.Position start = encoder.StartFixedLengthSize();
            encodeAction(encoder, in args);
            _ = encoder.EndFixedLengthSize(start);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a response from a remote exception.</summary>
        /// <param name="exception">The remote exception.</param>
        /// <returns>A new payload.</returns>
        public ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromRemoteException(RemoteException exception)
        {
            var bufferWriter = new BufferWriter();
            IceEncoder encoder = CreateIceEncoder(bufferWriter);

            BufferWriter.Position start = encoder.StartFixedLengthSize();
            encoder.EncodeException(exception);
            _ = encoder.EndFixedLengthSize(start);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value tuple. Use this
        /// method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's return value tuple.</typeparam>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{TEncoder, T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <returns>A new payload.</returns>
        public ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromReturnValueTuple<T>(
            in T returnValueTuple,
            TupleEncodeAction<IceEncoder, T> encodeAction) where T : struct
        {
            var bufferWriter = new BufferWriter();
            IceEncoder encoder = CreateIceEncoder(bufferWriter);
            BufferWriter.Position start = encoder.StartFixedLengthSize();
            encodeAction(encoder, in returnValueTuple);
            _ = encoder.EndFixedLengthSize(start);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value. Use this method
        /// when the operation returns a single value.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="returnValue">The return value to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{TEncoder, T}"/> that encodes the argument into the
        /// payload.</param>
        /// <returns>A new payload.</returns>
        public ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromSingleReturnValue<T>(
            T returnValue,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            var bufferWriter = new BufferWriter();
            IceEncoder encoder = CreateIceEncoder(bufferWriter);
            BufferWriter.Position start = encoder.StartFixedLengthSize();
            encodeAction(encoder, returnValue);
            _ = encoder.EndFixedLengthSize(start);
            return bufferWriter.Finish();
        }

        /// <summary>Decodes the size of a payload segment from a buffer.</summary>
        internal abstract int DecodeSegmentSize(ReadOnlySpan<byte> buffer);

        /// <summary>Decodes the length of the size of a payload segment from a buffer.</summary>
        internal abstract int DecodeSegmentSizeLength(ReadOnlySpan<byte> buffer);

        internal abstract IIceDecoderFactory<IceDecoder> GetIceDecoderFactory(
            FeatureCollection features,
            DefaultIceDecoderFactories defaultIceDecoderFactories);

        /// <summary>Creates an Ice encoder for this encoding.</summary>
        /// <param name="bufferWriter">The buffer writer.</param>
        /// <returns>A new encoder for the specified Ice encoding.</returns>
        internal abstract IceEncoder CreateIceEncoder(BufferWriter bufferWriter);

        private protected IceEncoding(string name)
            : base(name)
        {
        }
    }
}
