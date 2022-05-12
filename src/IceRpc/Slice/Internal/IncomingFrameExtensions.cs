// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode the payload of an incoming frame when this payload is encoded with the
    /// Slice encoding.</summary>
    internal static class IncomingFrameExtensions
    {
        /// <summary>Decodes arguments or a response value from a pipe reader.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="decodePayloadOptions">The decode payload options.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="defaultInvoker">The default invoker.</param>
        /// <param name="decodeFunc">The decode function for the payload arguments or return value.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The decode value.</returns>
        internal static ValueTask<T> DecodeValueAsync<T>(
            this IncomingFrame frame,
            SliceEncoding encoding,
            SliceDecodePayloadOptions decodePayloadOptions,
            IActivator? defaultActivator,
            IInvoker defaultInvoker,
            DecodeFunc<T> decodeFunc,
            CancellationToken cancel)
        {
            return frame.Payload.TryReadSegment(
                encoding,
                maxSize: 4_000_000, // TODO: configuration
                out ReadResult readResult) ? new(DecodeSegment(readResult)) :
                PerformDecodeAsync();

            // All the logic is in this local function.
            T DecodeSegment(ReadResult readResult)
            {
                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                var decoder = new SliceDecoder(
                    readResult.Buffer,
                    encoding,
                    frame.Connection,
                    decodePayloadOptions.ProxyInvoker ?? defaultInvoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    maxDepth: decodePayloadOptions.MaxDepth);
                T value = decodeFunc(ref decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);

                frame.Payload.AdvanceTo(readResult.Buffer.End);
                return value;
            }

            async ValueTask<T> PerformDecodeAsync() =>
                DecodeSegment(await frame.Payload.ReadSegmentAsync(
                    encoding,
                    maxSize: 4_000_000, // TODO: configuration
                    cancel).ConfigureAwait(false));
        }

        /// <summary>Reads/decodes empty args or a void return value.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="cancel">The cancellation token.</param>
        internal static ValueTask DecodeVoidAsync(
            this IncomingFrame frame,
            SliceEncoding encoding,
            CancellationToken cancel)
        {
            if (frame.Payload.TryReadSegment(encoding, maxSize: 4_000_000, out ReadResult readResult))
            {
                DecodeSegment(readResult);
                return default;
            }

            return PerformDecodeAsync();

            // All the logic is in this local function.
            void DecodeSegment(ReadResult readResult)
            {
                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (!readResult.Buffer.IsEmpty)
                {
                    var decoder = new SliceDecoder(readResult.Buffer, encoding);
                    decoder.CheckEndOfBuffer(skipTaggedParams: true);
                }
                frame.Payload.AdvanceTo(readResult.Buffer.End);
            }

            async ValueTask PerformDecodeAsync() =>
                DecodeSegment(await frame.Payload.ReadSegmentAsync(
                    encoding,
                    maxSize: 4_000_000,
                    cancel).ConfigureAwait(false));
        }

        /// <summary>Creates an async enumerable over a pipe reader to decode streamed members.</summary>
        /// <param name="frame">The incoming frame.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="decodePayloadOptions">The decode payload options.</param>
        /// <param name="defaultInvoker">The default invoker.</param>
        /// <param name="defaultActivator">The optional default activator.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        /// <param name="elementSize">The size in bytes of the streamed elements, or -1 for variable size elements.</param>
        /// <returns>The async enumerable to decode and return the streamed members.</returns>
        internal static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this IncomingFrame frame,
            SliceEncoding encoding,
            SliceDecodePayloadOptions decodePayloadOptions,
            IActivator? defaultActivator,
            IInvoker defaultInvoker,
            DecodeFunc<T> decodeFunc,
            int elementSize)
        {
            if(elementSize < 0 && elementSize != -1)
            {
                throw new ArgumentException($"element size must be greater than 0, or -1 for variable size elements");
            }
            IConnection connection = frame.Connection;
            var streamDecoder = new StreamDecoder<T>(DecodeBufferFunc, decodePayloadOptions.StreamDecoderOptions);

            PipeReader payload = frame.Payload;
            frame.Payload = InvalidPipeReader.Instance; // payload is now our responsibility

            // We read the payload and fill the writer (streamDecoder) in a separate thread. We don't give the frame to
            // this thread since frames are not thread-safe.
            _ = Task.Run(
                () =>_ = FillWriterAsync(payload, encoding, streamDecoder, elementSize),
                CancellationToken.None);

            // when CancelPendingRead is called on reader, ReadSegmentAsync returns a ReadResult with IsCanceled
            // set to true.
            return streamDecoder.ReadAsync(() => payload.CancelPendingRead());

            IEnumerable<T> DecodeBufferFunc(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    connection,
                    decodePayloadOptions.ProxyInvoker ?? defaultInvoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    maxDepth: decodePayloadOptions.MaxDepth);

                var items = new List<T>();
                do
                {
                    items.Add(decodeFunc(ref decoder));
                }
                while (decoder.Consumed < buffer.Length);

                return items;
            }

            async static Task FillWriterAsync(
                PipeReader payload,
                SliceEncoding encoding,
                StreamDecoder<T> streamDecoder,
                int elementSize)
            {
                while (true)
                {
                    // Each iteration decodes a segment with n values.

                    // If the reader of the async enumerable misbehaves, we can be left "hanging" in a paused
                    // streamDecoder.WriteAsync. The fix is to fix the application code: set the cancellation token
                    // with WithCancellation and cancel when the async enumerable reader is done and the iteration is
                    // not over (= streamDecoder writer is not completed).
                    CancellationToken cancel = CancellationToken.None;

                    ReadResult readResult;
                    bool streamReaderCompleted = false;
                    if (elementSize > 0)
                    {
                        try
                        {
                            readResult = await payload.ReadAsync(cancel).ConfigureAwait(false);

                        }
                        catch (Exception ex)
                        {
                            streamDecoder.CompleteWriter();
                            await payload.CompleteAsync(ex).ConfigureAwait(false);
                            break; // done
                        }

                        if (readResult.IsCanceled)
                        {
                            streamDecoder.CompleteWriter();

                            var ex = new OperationCanceledException();
                            await payload.CompleteAsync(ex).ConfigureAwait(false);
                            break; // done
                        }

                        if (readResult.Buffer.Length < elementSize)
                        {
                            payload.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        }
                        else
                        {
                            try
                            {
                                long remaining = readResult.Buffer.Length % elementSize;
                                var buffer = readResult.Buffer.Slice(0, readResult.Buffer.Length - remaining);
                                streamReaderCompleted =
                                    await streamDecoder.WriteAsync(buffer, cancel).ConfigureAwait(false);
                                payload.AdvanceTo(buffer.End);
                            }
                            catch (Exception ex)
                            {
                                streamDecoder.CompleteWriter();
                                await payload.CompleteAsync(ex).ConfigureAwait(false);
                                break;
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            readResult = await payload.ReadSegmentAsync(
                                encoding,
                                maxSize: 4_000_000, // TODO: configuration
                                cancel).ConfigureAwait(false);

                        }
                        catch (Exception ex)
                        {
                            streamDecoder.CompleteWriter();
                            await payload.CompleteAsync(ex).ConfigureAwait(false);
                            break; // done
                        }

                        if (readResult.IsCanceled)
                        {
                            streamDecoder.CompleteWriter();

                            var ex = new OperationCanceledException();
                            await payload.CompleteAsync(ex).ConfigureAwait(false);
                            break; // done
                        }

                        if (!readResult.Buffer.IsEmpty)
                        {
                            try
                            {
                                streamReaderCompleted = await streamDecoder.WriteAsync(
                                    readResult.Buffer,
                                    cancel).ConfigureAwait(false);

                                payload.AdvanceTo(readResult.Buffer.End);
                            }
                            catch (Exception ex)
                            {
                                streamDecoder.CompleteWriter();
                                await payload.CompleteAsync(ex).ConfigureAwait(false);
                                break;
                            }
                        }
                    }

                    if (streamReaderCompleted || readResult.IsCompleted)
                    {
                        streamDecoder.CompleteWriter();
                        await payload.CompleteAsync().ConfigureAwait(false);
                        break;
                    }
                }
            }
        }
    }
}
