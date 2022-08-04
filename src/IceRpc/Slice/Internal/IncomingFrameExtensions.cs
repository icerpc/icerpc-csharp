// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal;

/// <summary>Extension methods to decode the payload of an incoming frame when this payload is encoded with the
/// Slice encoding.</summary>
internal static class IncomingFrameExtensions
{
    /// <summary>Decodes arguments or a response value from a pipe reader.</summary>
    /// <param name="frame">The incoming frame.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="feature">The Slice feature.</param>
    /// <param name="activator">The activator.</param>
    /// <param name="serviceProxyFactory">The service proxy factory.</param>
    /// <param name="decodeFunc">The decode function for the payload arguments or return value.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The decode value.</returns>
    internal static ValueTask<T> DecodeValueAsync<T>(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature feature,
        IActivator? activator,
        Func<ServiceAddress, ServiceProxy> serviceProxyFactory,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancel)
    {
        return frame.Payload.TryReadSegment(
            encoding,
            feature.MaxSegmentSize,
            out ReadResult readResult) ? new(DecodeSegment(readResult)) :
            PerformDecodeAsync();

        // All the logic is in this local function.
        T DecodeSegment(ReadResult readResult)
        {
            readResult.ThrowIfCanceled(frame.Protocol, readingRequest: frame is IncomingRequest);

            var decoder = new SliceDecoder(
                readResult.Buffer,
                encoding,
                activator,
                serviceProxyFactory,
                feature.MaxCollectionAllocation,
                feature.MaxDepth);
            T value = decodeFunc(ref decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: true);

            frame.Payload.AdvanceTo(readResult.Buffer.End);
            return value;
        }

        async ValueTask<T> PerformDecodeAsync() =>
            DecodeSegment(await frame.Payload.ReadSegmentAsync(
                encoding,
                feature.MaxSegmentSize,
                cancel).ConfigureAwait(false));
    }

    /// <summary>Reads/decodes empty args or a void return value.</summary>
    /// <param name="frame">The incoming frame.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="feature">The Slice feature.</param>
    /// <param name="cancel">The cancellation token.</param>
    internal static ValueTask DecodeVoidAsync(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature feature,
        CancellationToken cancel)
    {
        if (frame.Payload.TryReadSegment(
                encoding,
                feature.MaxSegmentSize,
                out ReadResult readResult))
        {
            DecodeSegment(readResult);
            return default;
        }

        return PerformDecodeAsync();

        // All the logic is in this local function.
        void DecodeSegment(ReadResult readResult)
        {
            readResult.ThrowIfCanceled(frame.Protocol, readingRequest: frame is IncomingRequest);

            if (!readResult.Buffer.IsEmpty)
            {
                // no need to pass maxCollectionAllocation and other args since the only thing this decoding can
                // do is skip unknown tags
                var decoder = new SliceDecoder(readResult.Buffer, encoding);
                decoder.CheckEndOfBuffer(skipTaggedParams: true);
            }
            frame.Payload.AdvanceTo(readResult.Buffer.End);
        }

        async ValueTask PerformDecodeAsync() =>
            DecodeSegment(await frame.Payload.ReadSegmentAsync(
                encoding,
                feature.MaxSegmentSize,
                cancel).ConfigureAwait(false));
    }

    /// <summary>Creates an async enumerable over a pipe reader to decode streamed members.</summary>
    /// <param name="frame">The incoming frame.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="feature">The Slice feature.</param>
    /// <param name="activator">The activator.</param>
    /// <param name="serviceProxyFactory">The service proxy factory.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    internal static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature feature,
        IActivator? activator,
        Func<ServiceAddress, ServiceProxy> serviceProxyFactory,
        DecodeFunc<T> decodeFunc)
    {
        var streamDecoder = new StreamDecoder<T>(
            DecodeBufferFunc,
            feature.StreamPauseWriterThreshold,
            feature.StreamPauseWriterThreshold);

        PipeReader payload = frame.Payload;
        frame.Payload = InvalidPipeReader.Instance; // payload is now our responsibility

        // We read the payload and fill the writer (streamDecoder) in a separate thread. We don't give the frame to
        // this thread since frames are not thread-safe.
        _ = Task.Run(
            () => FillWriterAsync(
                frame,
                payload,
                encoding,
                feature,
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
                activator,
                serviceProxyFactory,
                feature.MaxCollectionAllocation,
                feature.MaxDepth);

            var items = new List<T>();
            do
            {
                items.Add(decodeFunc(ref decoder));
            }
            while (decoder.Consumed < buffer.Length);

            return items;
        }

        async static Task FillWriterAsync(
            IncomingFrame frame,
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
                // not over (= streamDecoder writer is not completed).
                CancellationToken cancel = CancellationToken.None;

                ReadResult readResult;
                bool streamReaderCompleted = false;
                try
                {
                    readResult = await payload.ReadSegmentAsync(
                        encoding,
                        feature.MaxSegmentSize,
                        cancel).ConfigureAwait(false);

                    readResult.ThrowIfCanceled(frame.Protocol, readingRequest: frame is IncomingRequest);

                    if (!readResult.Buffer.IsEmpty)
                    {
                        streamReaderCompleted = await streamDecoder.WriteAsync(
                            readResult.Buffer,
                            cancel).ConfigureAwait(false);

                        payload.AdvanceTo(readResult.Buffer.End);
                    }
                }
                catch (Exception ex)
                {
                    streamDecoder.CompleteWriter();
                    await payload.CompleteAsync(ex).ConfigureAwait(false);
                    break; // done
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

    /// <summary>Creates an async enumerable over a pipe reader to decode streamed members.</summary>
    /// <param name="frame">The incoming frame.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="feature">The Slice feature.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="elementSize">The size in bytes of one element.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    internal static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature feature,
        DecodeFunc<T> decodeFunc,
        int elementSize)
    {
        if (elementSize <= 0)
        {
            throw new ArgumentException("element size must be greater than 0");
        }

        feature ??= SliceFeature.Default;

        var streamDecoder = new StreamDecoder<T>(
            DecodeBufferFunc,
            feature.StreamPauseWriterThreshold,
            feature.StreamResumeWriterThreshold);

        PipeReader payload = frame.Payload;
        frame.Payload = InvalidPipeReader.Instance; // payload is now our responsibility

        // We read the payload and fill the writer (streamDecoder) in a separate thread. We don't give the frame to
        // this thread since frames are not thread-safe.
        _ = Task.Run(
            () => _ = FillWriterAsync(frame, payload, encoding, feature, streamDecoder, elementSize),
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
                maxCollectionAllocation: feature.MaxCollectionAllocation,
                maxDepth: feature.MaxDepth);

            var items = new List<T>();
            do
            {
                items.Add(decodeFunc(ref decoder));
            }
            while (decoder.Consumed < buffer.Length);

            return items;
        }

        async static Task FillWriterAsync(
            IncomingFrame frame,
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
                // not over (= streamDecoder writer is not completed).
                CancellationToken cancel = CancellationToken.None;

                ReadResult readResult;
                bool streamReaderCompleted = false;

                try
                {
                    readResult = await payload.ReadAsync(cancel).ConfigureAwait(false);
                    readResult.ThrowIfCanceled(frame.Protocol, readingRequest: frame is IncomingRequest);
                }
                catch (Exception ex)
                {
                    streamDecoder.CompleteWriter();
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
