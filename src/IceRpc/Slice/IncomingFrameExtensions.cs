// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods for <see cref="IncomingFrame"/>.</summary>
public static class IncomingFrameExtensions
{
    /// <summary>Detaches the payload from the incoming frame. The caller takes ownership of the returned payload
    /// pipe reader, and <see cref="IncomingFrame.Payload"/> becomes invalid.</summary>
    /// <param name="incoming">The incoming frame.</param>
    /// <returns>The payload pipe reader.</returns>
    public static PipeReader DetachPayload(this IncomingFrame incoming)
    {
        PipeReader payload = incoming.Payload;
        incoming.Payload = InvalidPipeReader.Instance;
        return payload;
    }

    /// <summary>Creates an async enumerable over a pipe reader to decode streamed members.</summary>
    /// <typeparam name="T">The type of the element being decoded.</typeparam>
    /// <param name="payload">The incoming frame payload.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="elementSize">The size in bytes of one element.</param>
    /// <param name="sliceFeature">The Slice feature to customize the decoding.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this PipeReader payload,
        SliceEncoding encoding,
        DecodeFunc<T> decodeFunc,
        int elementSize,
        ISliceFeature? sliceFeature = null)
    {
        if (elementSize <= 0)
        {
            throw new ArgumentException("element size must be greater than 0");
        }

        sliceFeature ??= SliceFeature.Default;

        var streamDecoder = new StreamDecoder<T>(
            DecodeBufferFunc,
            sliceFeature.StreamPauseWriterThreshold,
            sliceFeature.StreamResumeWriterThreshold);

        // We read the payload and fill the writer (streamDecoder) in a separate thread. We don't give the frame to
        // this thread since frames are not thread-safe.
        _ = Task.Run(
            () => _ = FillWriterAsync(payload, encoding, sliceFeature, streamDecoder, elementSize),
            CancellationToken.None);

        // when CancelPendingRead is called on reader, ReadSegmentAsync returns a ReadResult with IsCanceled
        // set to true.
        return streamDecoder.ReadAsync(() => payload.CancelPendingRead());

        IEnumerable<T> DecodeBufferFunc(ReadOnlySequence<byte> buffer)
        {
            // Since the elements are fixed-size, they can't contain proxies or instances created by an activator, hence
            // both activator and serviceProxyFactory can remain null.
            var decoder = new SliceDecoder(
                buffer,
                encoding,
                maxCollectionAllocation: sliceFeature.MaxCollectionAllocation,
                maxDepth: sliceFeature.MaxDepth);

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
            ISliceFeature feature,
            StreamDecoder<T> streamDecoder,
            int elementSize)
        {
            while (true)
            {
                // Each iteration decodes n values of fixed size elementSize.

                // If the reader of the async enumerable misbehaves, we can be left "hanging" in a paused
                // streamDecoder.WriteAsync. The fix is to fix the application code: set the cancellation token
                // with WithCancellation and cancel when the async enumerable reader is done and the iteration is
                // not over (= streamDecoder writer is not completed). The cancellation of this async enumerable reader
                // unblocks the streamDecoder.WriteAsync.
                CancellationToken cancellationToken = CancellationToken.None;

                ReadResult readResult;
                bool streamReaderCompleted = false;

                try
                {
                    readResult = await payload.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    streamDecoder.CompleteWriter(exception);
                    await payload.CompleteAsync(exception).ConfigureAwait(false);
                    break; // done
                }

                if (!readResult.IsCanceled)
                {
                    if (readResult.Buffer.Length < elementSize)
                    {
                        payload.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    }
                    else
                    {
                        try
                        {
                            long remaining = readResult.Buffer.Length % elementSize;
                            ReadOnlySequence<byte> buffer = readResult.Buffer.Slice(
                                0,
                                readResult.Buffer.Length - remaining);
                            streamReaderCompleted =
                                await streamDecoder.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                            payload.AdvanceTo(buffer.End);
                        }
                        catch (Exception exception)
                        {
                            streamDecoder.CompleteWriter(exception);
                            await payload.CompleteAsync(exception).ConfigureAwait(false);
                            break;
                        }
                    }
                }

                // readResult is canceled when the application cancels the async enumerable using WithCancellation; in
                // this case, we just want to exit and not report any exception.
                if (streamReaderCompleted || readResult.IsCanceled || readResult.IsCompleted)
                {
                    streamDecoder.CompleteWriter();
                    await payload.CompleteAsync().ConfigureAwait(false);
                    break;
                }
            }
        }
    }

    /// <summary>Creates an async enumerable over the payload reader of an incoming request to decode variable size
    /// streamed elements.</summary>
    /// <typeparam name="T">The stream element type.</typeparam>
    /// <param name="payload">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="defaultActivator">The activator to use when the activator of the Slice feature is null.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="templateProxy">The template proxy.</param>
    /// <param name="sliceFeature">The slice feature to customize the decoding.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this PipeReader payload,
        SliceEncoding encoding,
        IActivator? defaultActivator,
        DecodeFunc<T> decodeFunc,
        ServiceProxy? templateProxy = null,
        ISliceFeature? sliceFeature = null)
    {
        sliceFeature ??= SliceFeature.Default;
        var streamDecoder = new StreamDecoder<T>(
            DecodeBufferFunc,
            sliceFeature.StreamPauseWriterThreshold,
            sliceFeature.StreamPauseWriterThreshold);

        // We read the payload and fill the writer (streamDecoder) in a separate thread. We don't give the frame to
        // this thread since frames are not thread-safe.
        _ = Task.Run(
            () => FillWriterAsync(
                payload,
                encoding,
                sliceFeature,
                streamDecoder),
            CancellationToken.None);

        // when CancelPendingRead is called on reader, ReadSegmentAsync returns a ReadResult with IsCanceled
        // set to true.
        return streamDecoder.ReadAsync(() => payload.CancelPendingRead());

        IEnumerable<T> DecodeBufferFunc(ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(
                buffer,
                encoding,
                sliceFeature.Activator ?? defaultActivator,
                sliceFeature.ServiceProxyFactory,
                templateProxy,
                sliceFeature.MaxCollectionAllocation,
                sliceFeature.MaxDepth);

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
            ISliceFeature feature,
            StreamDecoder<T> streamDecoder)
        {
            while (true)
            {
                // Each iteration decodes a segment with n values.

                // If the reader of the async enumerable misbehaves, we can be left "hanging" in a paused
                // streamDecoder.WriteAsync. The fix is to fix the application code: set the cancellation token
                // with WithCancellation and cancel when the async enumerable reader is done and the iteration is
                // not over (= streamDecoder writer is not completed). The cancellation of this async enumerable reader
                // unblocks the streamDecoder.WriteAsync.
                CancellationToken cancellationToken = CancellationToken.None;

                ReadResult readResult;
                bool streamReaderCompleted = false;
                try
                {
                    readResult = await payload.ReadSegmentAsync(
                        encoding,
                        feature.MaxSegmentSize,
                        cancellationToken).ConfigureAwait(false);

                    if (!readResult.Buffer.IsEmpty)
                    {
                        streamReaderCompleted = await streamDecoder.WriteAsync(
                            readResult.Buffer,
                            cancellationToken).ConfigureAwait(false);

                        payload.AdvanceTo(readResult.Buffer.End);
                    }
                }
                catch (Exception exception)
                {
                    streamDecoder.CompleteWriter(exception);
                    await payload.CompleteAsync(exception).ConfigureAwait(false);
                    break; // done
                }

                // readResult is canceled when the application cancels the async enumerable using WithCancellation; in
                // this case, we just want to exit and not report any exception.
                if (streamReaderCompleted || readResult.IsCanceled || readResult.IsCompleted)
                {
                    streamDecoder.CompleteWriter();
                    await payload.CompleteAsync().ConfigureAwait(false);
                    break;
                }
            }
        }
    }
}
