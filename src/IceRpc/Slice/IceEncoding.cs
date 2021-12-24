// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.InteropServices;

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
        /// <param name="hasStream">When true, the Slice operation includes a stream in addition to the empty parameters
        /// or void return.</param>
        /// <returns>A new empty payload.</returns>
        // TODO: for now, we assume there is always a stream after. Fix with outgoing stream refactoring.
        public abstract PipeReader CreateEmptyPayload(bool hasStream = true);

        /// <summary>Creates the payload of a request from the request's argument. Use this method when the operation
        /// takes a single parameter.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="arg">The argument to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{T}"/> that encodes the argument into the
        /// payload.</param>
        /// <returns>A new payload.</returns>
        public PipeReader CreatePayloadFromSingleArg<T>(
            T arg,
            EncodeAction<T> encodeAction)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, this);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, arg);
            IceEncoder.EncodeFixedLengthSize(this, encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a request from the request's arguments. Use this method is for operations
        /// with multiple parameters.</summary>
        /// <typeparam name="T">The type of the operation's parameters.</typeparam>
        /// <param name="args">The arguments to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <returns>A new payload.</returns>
        public PipeReader CreatePayloadFromArgs<T>(
            in T args,
            TupleEncodeAction<T> encodeAction) where T : struct
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, this);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, in args);
            IceEncoder.EncodeFixedLengthSize(this, encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates a payload source stream from an async enumerable.</summary>
        public PipeReader CreatePayloadSourceStream<T>(
            IAsyncEnumerable<T> asyncEnumerable,
            EncodeAction<T> encodeAction)
        {
            var pipe = new Pipe(); // TODO: pipe options, pipe pooling

            // start writing immediately into background
            Task.Run(() => FillPipeAsync());

            return pipe.Reader;

            async Task FillPipeAsync()
            {
                PipeWriter writer = pipe.Writer;

                using var cancelationSource = new CancellationTokenSource();
                IAsyncEnumerator<T> asyncEnumerator = asyncEnumerable.GetAsyncEnumerator(cancelationSource.Token);
                await using var _ = asyncEnumerator.ConfigureAwait(false);

                Memory<byte> sizePlaceholder = StartSegment();
                int size = 0;

                while (true)
                {
                    ValueTask<bool> moveNext = asyncEnumerator.MoveNextAsync();
                    if (moveNext.IsCompletedSuccessfully)
                    {
                        if (moveNext.Result)
                        {
                            size += EncodeElement(asyncEnumerator.Current);
                        }
                        else
                        {
                            if (size > 0)
                            {
                                await FinishSegmentAsync(size, sizePlaceholder).ConfigureAwait(false);
                            }
                            break; // End iteration
                        }
                    }
                    else
                    {
                        // If we already wrote some elements write the segment now and start a new one.
                        if (size > 0)
                        {
                            FlushResult flushResult = await FinishSegmentAsync(
                                size,
                                sizePlaceholder).ConfigureAwait(false);

                            // nobody can call CancelPendingFlush on this writer
                            Debug.Assert(!flushResult.IsCanceled);

                            if (flushResult.IsCompleted) // reader no longer reading
                            {
                                cancelationSource.Cancel();
                                break; // End iteration
                            }

                            sizePlaceholder = StartSegment();
                            size = 0;
                        }

                        if (await moveNext.ConfigureAwait(false))
                        {
                            size += EncodeElement(asyncEnumerator.Current);
                        }
                        else
                        {
                            break; // End iteration
                        }
                    }

                    // TODO allow to configure the size limit?
                    if (size > 32 * 1024)
                    {
                        FlushResult flushResult = await FinishSegmentAsync(
                                size,
                                sizePlaceholder).ConfigureAwait(false);

                        // nobody can call CancelPendingFlush on this writer
                        Debug.Assert(!flushResult.IsCanceled);

                        if (flushResult.IsCompleted) // reader no longer reading
                        {
                            break; // End iteration
                        }

                        // TODO: why all this duplicated code??
                        sizePlaceholder = StartSegment();
                        size = 0;
                    }
                }

                // Write end of stream
                await writer.CompleteAsync().ConfigureAwait(false);

                int EncodeElement(T element)
                {
                    // TODO: An encoder is very lightweight, however, creating an encoder per element seems extreme
                    // for tiny elements.
                    var encoder = new IceEncoder(writer, this);
                    encodeAction(ref encoder, element);
                    return encoder.EncodedByteCount;
                }

                Memory<byte> StartSegment()
                {
                    Memory<byte> sizePlaceholder = writer.GetMemory(4)[0..4];
                    writer.Advance(4);
                    return sizePlaceholder;
                }

                async ValueTask<FlushResult> FinishSegmentAsync(
                    int size,
                    Memory<byte> sizePlaceholder)
                {
                    IceEncoder.EncodeFixedLengthSize(this, size, sizePlaceholder.Span);
                    try
                    {
                        return await writer.FlushAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        cancelationSource.Cancel();
                        await writer.CompleteAsync(ex).ConfigureAwait(false);
                        throw;
                    }
                }
            }
        }

        /// <summary>Creates the payload of a response from a remote exception.</summary>
        /// <param name="exception">The remote exception.</param>
        /// <returns>A new payload.</returns>
        public PipeReader CreatePayloadFromRemoteException(RemoteException exception)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, this);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeException(exception);
            IceEncoder.EncodeFixedLengthSize(this, encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value tuple. Use this
        /// method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's return value tuple.</typeparam>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <returns>A new payload.</returns>
        public PipeReader CreatePayloadFromReturnValueTuple<T>(
            in T returnValueTuple,
            TupleEncodeAction<T> encodeAction) where T : struct
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, this);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, in returnValueTuple);
            IceEncoder.EncodeFixedLengthSize(this, encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value. Use this method
        /// when the operation returns a single value.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="returnValue">The return value to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{T}"/> that encodes the argument into the
        /// payload.</param>
        /// <returns>A new payload.</returns>
        public PipeReader CreatePayloadFromSingleReturnValue<T>(
            T returnValue,
            EncodeAction<T> encodeAction)
        {
            var pipe = new Pipe(); // TODO: pipe options

            var encoder = new IceEncoder(pipe.Writer, this);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction(ref encoder, returnValue);
            IceEncoder.EncodeFixedLengthSize(this, encoder.EncodedByteCount - startPos, sizePlaceholder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Decodes the size of a segment read from a PipeReader.</summary>
        internal abstract ValueTask<(int Size, bool IsCanceled, bool IsCompleted)> DecodeSegmentSizeAsync(
            PipeReader reader,
            CancellationToken cancel);

        internal static int DecodeInt(ReadOnlySpan<byte> from) => BitConverter.ToInt32(from);

        // Applies to all var type: varlong, varulong etc.
        internal static int DecodeVarLongLength(byte from) => 1 << (from & 0x03);

        internal static (ulong Value, int ValueLength) DecodeVarULong(ReadOnlySpan<byte> from)
        {
            ulong value = (from[0] & 0x03) switch
            {
                0 => (uint)from[0] >> 2,
                1 => (uint)BitConverter.ToUInt16(from) >> 2,
                2 => BitConverter.ToUInt32(from) >> 2,
                _ => BitConverter.ToUInt64(from) >> 2
            };

            return (value, DecodeVarLongLength(from[0]));
        }

        private protected IceEncoding(string name)
            : base(name)
        {
        }
    }
}
