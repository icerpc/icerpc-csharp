// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

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
        /// <param name="encodeAction">The <see cref="EncodeAction{TEncoder, T}"/> that encodes the argument into the
        /// payload.</param>
        /// <returns>A new payload.</returns>
        public PipeReader CreatePayloadFromSingleArg<T>(
            T arg,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            var pipe = new Pipe(); // TODO: pipe options

            IceEncoder encoder = CreateIceEncoder(pipe.Writer);
            Span<byte> sizePlaceHolder = encoder.GetPlaceHolderSpan(4);
            int startPos = encoder.EncodedBytes;
            encodeAction(encoder, arg);
            encoder.EncodeFixedLengthSize(encoder.EncodedBytes - startPos, sizePlaceHolder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a request from the request's arguments. Use this method is for operations
        /// with multiple parameters.</summary>
        /// <typeparam name="T">The type of the operation's parameters.</typeparam>
        /// <param name="args">The arguments to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{TEncoder, T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <returns>A new payload.</returns>
        public PipeReader CreatePayloadFromArgs<T>(
            in T args,
            TupleEncodeAction<IceEncoder, T> encodeAction) where T : struct
        {
            var pipe = new Pipe(); // TODO: pipe options

            IceEncoder encoder = CreateIceEncoder(pipe.Writer);
            Span<byte> sizePlaceHolder = encoder.GetPlaceHolderSpan(4);
            int startPos = encoder.EncodedBytes;
            encodeAction(encoder, in args);
            encoder.EncodeFixedLengthSize(encoder.EncodedBytes - startPos, sizePlaceHolder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates a payload source stream from an async enumerable.</summary>
        public PipeReader CreatePayloadSourceStream<T>(
            IAsyncEnumerable<T> asyncEnumerable,
            EncodeAction<IceEncoder, T> encodeAction)
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

                (IceEncoder encoder, int startPos, Memory<byte> sizePlaceHolder) = StartSegment();

                while (true)
                {
                    ValueTask<bool> moveNext = asyncEnumerator.MoveNextAsync();
                    if (moveNext.IsCompletedSuccessfully)
                    {
                        if (moveNext.Result)
                        {
                            encodeAction(encoder, asyncEnumerator.Current);
                        }
                        else
                        {
                            if (encoder.EncodedBytes != startPos)
                            {
                                await FinishSegmentAsync(encoder, startPos, sizePlaceHolder).ConfigureAwait(false);
                            }
                            break; // End iteration
                        }
                    }
                    else
                    {
                        // If we already wrote some elements write the segment now and start a new one.
                        if (encoder.EncodedBytes != startPos)
                        {
                            FlushResult flushResult = await FinishSegmentAsync(
                                encoder,
                                startPos,
                                sizePlaceHolder).ConfigureAwait(false);

                            // nobody can call CancelPendingFlush on this writer
                            Debug.Assert(!flushResult.IsCanceled);

                            if (flushResult.IsCompleted) // reader no longer reading
                            {
                                cancelationSource.Cancel();
                                break; // End iteration
                            }

                            (encoder, startPos, sizePlaceHolder) = StartSegment();
                        }

                        if (await moveNext.ConfigureAwait(false))
                        {
                            encodeAction(encoder, asyncEnumerator.Current);
                        }
                        else
                        {
                            break; // End iteration
                        }
                    }

                    // TODO allow to configure the size limit?
                    if (encoder.EncodedBytes > 32 * 1024)
                    {
                        FlushResult flushResult = await FinishSegmentAsync(
                                encoder,
                                startPos,
                                sizePlaceHolder).ConfigureAwait(false);

                        // nobody can call CancelPendingFlush on this writer
                        Debug.Assert(!flushResult.IsCanceled);

                        if (flushResult.IsCompleted) // reader no longer reading
                        {
                            break; // End iteration
                        }

                        (encoder, startPos, sizePlaceHolder) = StartSegment();
                    }
                }

                // Write end of stream
                await writer.CompleteAsync().ConfigureAwait(false);

                (IceEncoder Encoder, int StartPos, Memory<byte> SizePlaceHolder) StartSegment()
                {
                    IceEncoder encoder = CreateIceEncoder(writer);
                    Memory<byte> sizePlaceHolder = encoder.GetPlaceHolderMemory(4);
                    int startPos = encoder.EncodedBytes;
                    return (encoder, startPos, sizePlaceHolder);
                }

                async ValueTask<FlushResult> FinishSegmentAsync(
                    IceEncoder encoder,
                    int startPos,
                    Memory<byte> sizePlaceHolder)
                {
                    encoder.EncodeFixedLengthSize(encoder.EncodedBytes - startPos, sizePlaceHolder.Span);
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

            IceEncoder encoder = CreateIceEncoder(pipe.Writer);
            Span<byte> sizePlaceHolder = encoder.GetPlaceHolderSpan(4);
            int startPos = encoder.EncodedBytes;
            encoder.EncodeException(exception);
            encoder.EncodeFixedLengthSize(encoder.EncodedBytes - startPos, sizePlaceHolder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value tuple. Use this
        /// method when the operation returns a tuple.</summary>
        /// <typeparam name="T">The type of the operation's return value tuple.</typeparam>
        /// <param name="returnValueTuple">The return values to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="TupleEncodeAction{TEncoder, T}"/> that encodes the arguments into
        /// the payload.</param>
        /// <returns>A new payload.</returns>
        public PipeReader CreatePayloadFromReturnValueTuple<T>(
            in T returnValueTuple,
            TupleEncodeAction<IceEncoder, T> encodeAction) where T : struct
        {
            var pipe = new Pipe(); // TODO: pipe options

            IceEncoder encoder = CreateIceEncoder(pipe.Writer);
            Span<byte> sizePlaceHolder = encoder.GetPlaceHolderSpan(4);
            int startPos = encoder.EncodedBytes;
            encodeAction(encoder, in returnValueTuple);
            encoder.EncodeFixedLengthSize(encoder.EncodedBytes - startPos, sizePlaceHolder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Creates the payload of a response from the request's dispatch and return value. Use this method
        /// when the operation returns a single value.</summary>
        /// <typeparam name="T">The type of the operation's parameter.</typeparam>
        /// <param name="returnValue">The return value to write into the payload.</param>
        /// <param name="encodeAction">The <see cref="EncodeAction{TEncoder, T}"/> that encodes the argument into the
        /// payload.</param>
        /// <returns>A new payload.</returns>
        public PipeReader CreatePayloadFromSingleReturnValue<T>(
            T returnValue,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            var pipe = new Pipe(); // TODO: pipe options

            IceEncoder encoder = CreateIceEncoder(pipe.Writer);
            Span<byte> sizePlaceHolder = encoder.GetPlaceHolderSpan(4);
            int startPos = encoder.EncodedBytes;
            encodeAction(encoder, returnValue);
            encoder.EncodeFixedLengthSize(encoder.EncodedBytes - startPos, sizePlaceHolder);

            pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
            return pipe.Reader;
        }

        /// <summary>Decodes the size of a segment read from a PipeReader.</summary>
        internal abstract ValueTask<(int Size, bool IsCanceled, bool IsCompleted)> DecodeSegmentSizeAsync(
            PipeReader reader,
            CancellationToken cancel);

        internal abstract IIceDecoderFactory<IceDecoder> GetIceDecoderFactory(
            FeatureCollection features,
            DefaultIceDecoderFactories defaultIceDecoderFactories);

        /// <summary>Creates an Ice encoder for this encoding.</summary>
        /// <param name="bufferWriter">The buffer writer.</param>
        /// <returns>A new encoder for the specified Ice encoding.</returns>
        internal abstract IceEncoder CreateIceEncoder(IBufferWriter<byte> bufferWriter);

        private protected IceEncoding(string name)
            : base(name)
        {
        }
    }
}
