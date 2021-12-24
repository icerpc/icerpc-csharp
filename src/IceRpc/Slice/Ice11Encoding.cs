// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>The Ice 1.1 encoding class.</summary>
    public sealed class Ice11Encoding : IceEncoding
    {
        /// <summary>The Ice 1.1 encoding singleton.</summary>
        internal static Ice11Encoding Instance { get; } = new();

        /// <summary>Creates the payload of a request from the request's arguments. Use this method is for operations
        /// with multiple parameters.</summary>
        /// <typeparam name="T">The type of the operation's parameters.</typeparam>
        /// <param name="args">The arguments to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload encoded with encoding Ice 1.1.</returns>
        public static PipeReader CreatePayloadFromArgs<T>(
            in T args,
            TupleEncodeAction<T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, Encoding.Ice11, classFormat);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, in args);
            IceEncoder.EncodeInt(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a request from the request's argument. Use this method when the operation
        /// takes a single parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="arg">The argument to write into the payload.</param>
        /// <param name="encodeAction">A delegate that encodes the argument into the payload.</param>
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload encoded with encoding Ice 1.1.</returns>
        public static PipeReader CreatePayloadFromSingleArg<T>(
            T arg,
            EncodeAction<T> encodeAction,
            FormatType classFormat = default)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, Encoding.Ice11, classFormat);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, arg);
            IceEncoder.EncodeInt(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value tuple. Use this
        /// method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's return value tuple.</typeparam>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload encoded with the Ice 1.1 encoding.</returns>
        public static PipeReader CreatePayloadFromReturnValueTuple<T>(
            in T returnValueTuple,
            TupleEncodeAction<T> encodeAction,
            FormatType classFormat = default) where T : struct
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, Encoding.Ice11, classFormat);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, in returnValueTuple);
            IceEncoder.EncodeInt(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value. Use this method
        /// when the operation returns a single value.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="returnValue">The return value to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{T}"/> that encodes the argument into the
        /// payload.</param>
        /// <param name="classFormat">The class format.</param>
        /// <returns>A new payload with the Ice 1.1 encoding.</returns>
        public static PipeReader CreatePayloadFromSingleReturnValue<T>(
            T returnValue,
            EncodeAction<T> encodeAction,
            FormatType classFormat = default)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, Encoding.Ice11, classFormat);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, returnValue);
            IceEncoder.EncodeInt(encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        internal static int DecodeFixedLengthSize(ReadOnlySpan<byte> buffer)
        {
            int size = IceDecoder.DecodeInt(buffer);
            if (size < 0)
            {
                throw new InvalidDataException("received invalid negative size");
            }
            return size;
        }

        /// <summary>Encodes a variable-length size into a span.</summary>
        internal static void EncodeSize(int size, Span<byte> into)
        {
            if (size < 0)
            {
                throw new ArgumentException("a size must be positive", nameof(size));
            }

            if (into.Length == 1)
            {
                if (size >= 255)
                {
                    throw new ArgumentException("size value is too large for into", nameof(size));
                }

                into[0] = (byte)size;
            }
            else if (into.Length == 5)
            {
                into[0] = 255;
                IceEncoder.EncodeInt(size, into[1..]);
            }
            else
            {
                throw new ArgumentException("into's size must be 1 or 5", nameof(into));
            }
        }

        private Ice11Encoding()
            : base(Ice11Name)
        {
        }
    }
}
