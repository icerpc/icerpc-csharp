// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice
{
    /// <summary>Extension methods to decode the payload of an incoming request when this payload is encoded with the
    /// Slice encoding.</summary>
    public static class IncomingFrameExtensions
    {
        /// <summary>Verifies that a request payload carries no argument or only unknown tagged arguments.</summary>
        /// <param name="frame">The incoming request.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="hasStream"><c>true</c> if this void value is followed by a stream parameter;
        /// otherwise, <c>false</c>.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A value task that completes when the checking is complete.</returns>
        public static async ValueTask ReadVoidAsync(
            this IncomingFrame frame,
            SliceEncoding encoding,
            bool hasStream,
            CancellationToken cancel)
        {
            try
            {
                ReadResult readResult = await frame.Payload.ReadSegmentAsync(cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                if (!readResult.Buffer.IsEmpty)
                {
                    Decode(readResult.Buffer);
                    frame.Payload.AdvanceTo(readResult.Buffer.End);
                }

                if (!hasStream)
                {
                    // If there are actually additional bytes on the pipe reader, we ignore them. It's possible the
                    // sender operation Slice definition specifies a stream parameter that is not specified on the
                    // operation local Slice definition.
                    await frame.CompleteAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                await frame.CompleteAsync(exception).ConfigureAwait(false);
                throw;
            }

            void Decode(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(buffer, encoding);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
            }
        }

        /// <summary>Decodes the request's payload into a list of arguments.</summary>
        /// <paramtype name="T">The type of the request parameters.</paramtype>
        /// <param name="frame">The incoming request.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="decodePayloadOptions">The decode payload options.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="defaultInvoker">The default invoker.</param>
        /// <param name="decodeFunc">The decode function for the arguments from the payload.</param>
        /// <param name="hasStream">When true, T is or includes a stream.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>The request arguments.</returns>
        public static async ValueTask<T> ReadValueAsync<T>(
            this IncomingFrame frame,
            SliceEncoding encoding,
            SliceDecodePayloadOptions decodePayloadOptions,
            IActivator defaultActivator,
            IInvoker defaultInvoker,
            DecodeFunc<T> decodeFunc,
            bool hasStream,
            CancellationToken cancel)
        {
            try
            {
                ReadResult readResult = await frame.Payload.ReadSegmentAsync(cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException();
                }

                // The segment can be empty, for example args with only tagged parameters where the sender does not know
                // any tagged param or all the tagged params are null. We still decode such an empty segment to make
                // sure decodeFunc is fine with it.
                T result = Decode(readResult.Buffer);

                if (!readResult.Buffer.IsEmpty)
                {
                    frame.Payload.AdvanceTo(readResult.Buffer.End);
                }

                if (!hasStream)
                {
                    // If there are actually additional bytes on the pipe reader, we ignore them. It's possible the
                    // sender operation Slice definition specifies a stream parameter that is not specified on the
                    // operation local Slice definition.
                    await frame.CompleteAsync().ConfigureAwait(false);
                }
                return result;
            }
            catch (Exception exception)
            {
                await frame.CompleteAsync(exception).ConfigureAwait(false);
                throw;
            }

            T Decode(ReadOnlySequence<byte> buffer)
            {
                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    frame.Connection,
                    decodePayloadOptions.ProxyInvoker ?? defaultInvoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    decodePayloadOptions.MaxDepth);
                T value = decodeFunc(ref decoder);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
                return value;
            }
        }

        /// <summary>Creates an async enumerable over the payload reader of an incoming request.</summary>
        /// <param name="frame">The incoming request.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="decodePayloadOptions">The decode payload options.</param>
        /// <param name="defaultInvoker">The default invoker.</param>
        /// <param name="defaultActivator">The default activator.</param>
        /// <param name="decodeFunc">The function used to decode the streamed member.</param>
        public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this IncomingFrame frame,
            SliceEncoding encoding,
            SliceDecodePayloadOptions decodePayloadOptions,
            IActivator defaultActivator,
            IInvoker defaultInvoker,
            DecodeFunc<T> decodeFunc)
        {
            Func<ReadOnlySequence<byte>, IEnumerable<T>> decodeBufferFunc = buffer =>
            {
                var decoder = new SliceDecoder(
                    buffer,
                    encoding,
                    frame.Connection,
                    decodePayloadOptions.ProxyInvoker ?? defaultInvoker,
                    decodePayloadOptions.Activator ?? defaultActivator,
                    decodePayloadOptions.MaxDepth);

                var items = new List<T>();
                do
                {
                    items.Add(decodeFunc(ref decoder));
                }
                while (decoder.Consumed < buffer.Length);

                return items;
            };

            var streamDecoder = new StreamDecoder<T>(decodeBufferFunc, decodePayloadOptions.StreamDecoderOptions);

            _ = Task.Run(() => FillWriterAsync(), CancellationToken.None);

            // when CancelPendingRead is called on reader, ReadSegmentAsync returns a ReadResult with IsCanceled
            // set to true.
            return streamDecoder.ReadAsync(() => frame.Payload.CancelPendingRead());

            async Task FillWriterAsync()
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

                    try
                    {
                        readResult = await frame.Payload.ReadSegmentAsync(cancel).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        streamDecoder.CompleteWriter();
                        await frame.CompleteAsync(ex).ConfigureAwait(false);
                        break; // done
                    }

                    if (readResult.IsCanceled)
                    {
                        streamDecoder.CompleteWriter();

                        var ex = new OperationCanceledException();
                        await frame.CompleteAsync(ex).ConfigureAwait(false);
                        break; // done
                    }

                    bool streamReaderCompleted = false;

                    if (!readResult.Buffer.IsEmpty)
                    {
                        try
                        {
                            streamReaderCompleted = await streamDecoder.WriteAsync(
                                readResult.Buffer,
                                cancel).ConfigureAwait(false);

                            frame.Payload.AdvanceTo(readResult.Buffer.End);
                        }
                        catch (Exception ex)
                        {
                            streamDecoder.CompleteWriter();
                            await frame.CompleteAsync(ex).ConfigureAwait(false);
                            break;
                        }
                    }

                    if (streamReaderCompleted || readResult.IsCompleted)
                    {
                        streamDecoder.CompleteWriter();
                        await frame.CompleteAsync().ConfigureAwait(false);
                        break;
                    }
                }
            }
        }
    }
}
