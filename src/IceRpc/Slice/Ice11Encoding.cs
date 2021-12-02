// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>The Ice 1.1 encoding class.</summary>
    public sealed class Ice11Encoding : IceEncoding
    {
        /// <summary>The Ice 1.1 encoding singleton.</summary>
        internal static Ice11Encoding Instance { get; } = new();

        private static readonly ReadOnlyMemory<ReadOnlyMemory<byte>> _emptyPayload = new ReadOnlyMemory<byte>[]
        {
            new byte[] { 0, 0, 0, 0 } // 0 size on 4 bytes
        };

        /// <inheritdoc/>
        public override ReadOnlyMemory<ReadOnlyMemory<byte>> CreateEmptyPayload() => _emptyPayload;

        /// <summary>Creates the payload of a request from the request's arguments. Use this method is for operations
        /// with multiple parameters.</summary>
        /// <typeparam name="T">The type of the operation's parameters.</typeparam>
        /// <param name="args">The arguments to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{TEncoder, T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload encoded with encoding Ice 1.1.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromArgs<T>(
            in T args,
            TupleEncodeAction<Ice11Encoder, T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var bufferWriter = new BufferWriter();
            var encoder = new Ice11Encoder(bufferWriter, classFormat);
            BufferWriter.Position start = encoder.StartFixedLengthSize();
            encodeAction(encoder, in args);
            _ = encoder.EndFixedLengthSize(start);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a request from the request's argument. Use this method when the operation
        /// takes a single parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="arg">The argument to write into the payload.</param>
        /// <param name="encodeAction">A delegate that encodes the argument into the payload.</param>
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload encoded with encoding Ice 1.1.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromSingleArg<T>(
            T arg,
            Action<Ice11Encoder, T> encodeAction,
            FormatType classFormat = default)
        {
            var bufferWriter = new BufferWriter();
            var encoder = new Ice11Encoder(bufferWriter, classFormat);
            BufferWriter.Position start = encoder.StartFixedLengthSize();
            encodeAction(encoder, arg);
            _ = encoder.EndFixedLengthSize(start);
            return bufferWriter.Finish();
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value tuple. Use this
        /// method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's return value tuple.</typeparam>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{TEncoder, T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload encoded with the Ice 1.1 encoding.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromReturnValueTuple<T>(
            in T returnValueTuple,
            TupleEncodeAction<Ice11Encoder, T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var bufferWriter = new BufferWriter();
            var encoder = new Ice11Encoder(bufferWriter, classFormat);
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
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload with the Ice 1.1 encoding.</returns>
        public static ReadOnlyMemory<ReadOnlyMemory<byte>> CreatePayloadFromSingleReturnValue<T>(
            T returnValue,
            EncodeAction<Ice11Encoder, T> encodeAction,
            FormatType classFormat = default)
        {
            var bufferWriter = new BufferWriter();
            var encoder = new Ice11Encoder(bufferWriter, classFormat);
            BufferWriter.Position start = encoder.StartFixedLengthSize();
            encodeAction(encoder, returnValue);
            _ = encoder.EndFixedLengthSize(start);
            return bufferWriter.Finish();
        }

        internal override IceEncoder CreateIceEncoder(BufferWriter bufferWriter) => new Ice11Encoder(bufferWriter);

        internal override async ValueTask<int> DecodeSegmentSizeAsync(PipeReader reader, CancellationToken cancel)
        {
            const int sizeLength = 4;

            ReadResult readResult = await reader.ReadAtLeastAsync(sizeLength, cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            if (readResult.IsCompleted && readResult.Buffer.Length == 0)
            {
                return 0;
            }

            ReadOnlySequence<byte> buffer = readResult.Buffer.Slice(0, sizeLength);
            int size = DecodeSize(buffer, sizeLength);
            reader.AdvanceTo(buffer.End);
            return size;

            static int DecodeSize(ReadOnlySequence<byte> buffer, int sizeLength)
            {
                Span<byte> span = stackalloc byte[sizeLength];
                buffer.CopyTo(span);
                return IceDecoder.DecodeInt(span);
            }
        }

        internal override IIceDecoderFactory<IceDecoder> GetIceDecoderFactory(
            FeatureCollection features,
            DefaultIceDecoderFactories defaultIceDecoderFactories) =>
            features.Get<IIceDecoderFactory<Ice11Decoder>>() ?? defaultIceDecoderFactories.Ice11DecoderFactory;

        private Ice11Encoding()
            : base(Ice11Name)
        {
        }
    }
}
