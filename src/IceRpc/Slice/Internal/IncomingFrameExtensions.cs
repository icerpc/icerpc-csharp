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
    /// <param name="decodeFeature">The decode feature.</param>
    /// <param name="defaultActivator">The default activator.</param>
    /// <param name="proxyInvoker">The default invoker.</param>
    /// <param name="encodeOptions">The encode options of decoded proxy structs.</param>
    /// <param name="decodeFunc">The decode function for the payload arguments or return value.</param>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The decode value.</returns>
    internal static ValueTask<T> DecodeValueAsync<T>(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature? decodeFeature,
        IActivator? defaultActivator,
        IInvoker? proxyInvoker,
        SliceEncodeOptions? encodeOptions,
        DecodeFunc<T> decodeFunc,
        CancellationToken cancel)
    {
        decodeFeature ??= SliceDecodeFeature.Default;

        return frame.Payload.TryReadSegment(
            encoding,
            decodeFeature.MaxSegmentSize,
            out ReadResult readResult) ? new(DecodeSegment(readResult)) :
            PerformDecodeAsync();

        // All the logic is in this local function.
        T DecodeSegment(ReadResult readResult)
        {
            readResult.ThrowIfCanceled(frame.Protocol);

            var decoder = new SliceDecoder(
                readResult.Buffer,
                encoding,
                decodeFeature.Activator ?? defaultActivator,
                decodeFeature.ServiceProxyFactory,
                proxyInvoker,
                frame.ConnectionContext,
                encodeOptions,
                maxCollectionAllocation: decodeFeature.MaxCollectionAllocation,
                maxDepth: decodeFeature.MaxDepth);
            T value = decodeFunc(ref decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: true);

            frame.Payload.AdvanceTo(readResult.Buffer.End);
            return value;
        }

        async ValueTask<T> PerformDecodeAsync() =>
            DecodeSegment(await frame.Payload.ReadSegmentAsync(
                encoding,
                decodeFeature.MaxSegmentSize,
                cancel).ConfigureAwait(false));
    }

    /// <summary>Reads/decodes empty args or a void return value.</summary>
    /// <param name="frame">The incoming frame.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="decodeFeature">The decode feature.</param>
    /// <param name="cancel">The cancellation token.</param>
    internal static ValueTask DecodeVoidAsync(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature? decodeFeature,
        CancellationToken cancel)
    {
        decodeFeature ??= SliceDecodeFeature.Default;

        if (frame.Payload.TryReadSegment(
                encoding,
                decodeFeature.MaxSegmentSize,
                out ReadResult readResult))
        {
            DecodeSegment(readResult);
            return default;
        }

        return PerformDecodeAsync();

        // All the logic is in this local function.
        void DecodeSegment(ReadResult readResult)
        {
            readResult.ThrowIfCanceled(frame.Protocol);

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
                decodeFeature.MaxSegmentSize,
                cancel).ConfigureAwait(false));
    }

    /// <summary>Creates an async enumerable over a pipe reader to decode streamed members.</summary>
    /// <param name="frame">The incoming frame.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="decodeFeature">The decode feature.</param>
    /// <param name="defaultActivator">The optional default activator.</param>
    /// <param name="proxyInvoker">The invoker of the proxy that sent this connection when frame is an outgoing request.
    /// </param>
    /// <param name="encodeOptions">The encode options of decoded proxy structs.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    internal static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature? decodeFeature,
        IActivator? defaultActivator,
        IInvoker? proxyInvoker,
        SliceEncodeOptions? encodeOptions,
        DecodeFunc<T> decodeFunc)
    {
        decodeFeature ??= SliceDecodeFeature.Default;
        var streamDecoder = new StreamDecoder<T>(
            DecodeBufferFunc,
            decodeFeature.StreamPauseWriterThreshold,
            decodeFeature.StreamPauseWriterThreshold);

        PipeReader payload = frame.Payload;
        frame.Payload = InvalidPipeReader.Instance; // payload is now our responsibility

        // We read the payload and fill the writer (streamDecoder) in a separate thread. We don't give the frame to
        // this thread since frames are not thread-safe.
        _ = Task.Run(
            () => FillWriterAsync(
                frame.Protocol,
                payload,
                encoding,
                decodeFeature,
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
                decodeFeature.Activator ?? defaultActivator,
                decodeFeature.ServiceProxyFactory,
                proxyInvoker,
                frame.ConnectionContext,
                encodeOptions,
                maxCollectionAllocation: decodeFeature.MaxCollectionAllocation,
                maxDepth: decodeFeature.MaxDepth);

            var items = new List<T>();
            do
            {
                items.Add(decodeFunc(ref decoder));
            }
            while (decoder.Consumed < buffer.Length);

            return items;
        }

        async static Task FillWriterAsync(
            Protocol protocol,
            PipeReader payload,
            SliceEncoding encoding,
            ISliceFeature decodeFeature,
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
                        decodeFeature.MaxSegmentSize,
                        cancel).ConfigureAwait(false);

                    readResult.ThrowIfCanceled(protocol);

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
    /// <param name="decodeFeature">The decode feature.</param>
    /// <param name="defaultActivator">The optional default activator.</param>
    /// <param name="proxyInvoker">The default invoker.</param>
    /// <param name="proxyEncodeFeature">The encode options of decoded proxy structs.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="elementSize">The size in bytes of the streamed elements.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    internal static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature? decodeFeature,
        IActivator? defaultActivator,
        IInvoker? proxyInvoker,
        SliceEncodeOptions? proxyEncodeFeature,
        DecodeFunc<T> decodeFunc,
        int elementSize)
    {
        if (elementSize <= 0)
        {
            throw new ArgumentException("element size must be greater than 0");
        }

        decodeFeature ??= SliceDecodeFeature.Default;

        var streamDecoder = new StreamDecoder<T>(
            DecodeBufferFunc,
            decodeFeature.StreamPauseWriterThreshold,
            decodeFeature.StreamResumeWriterThreshold);

        PipeReader payload = frame.Payload;
        frame.Payload = InvalidPipeReader.Instance; // payload is now our responsibility

        // We read the payload and fill the writer (streamDecoder) in a separate thread. We don't give the frame to
        // this thread since frames are not thread-safe.
        _ = Task.Run(
            () => _ = FillWriterAsync(frame.Protocol, payload, encoding, decodeFeature, streamDecoder, elementSize),
            CancellationToken.None);

        // when CancelPendingRead is called on reader, ReadSegmentAsync returns a ReadResult with IsCanceled
        // set to true.
        return streamDecoder.ReadAsync(() => payload.CancelPendingRead());

        IEnumerable<T> DecodeBufferFunc(ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(
                buffer,
                encoding,
                decodeFeature.Activator ?? defaultActivator,
                decodeFeature.ServiceProxyFactory,
                proxyInvoker,
                frame.ConnectionContext,
                proxyEncodeFeature,
                maxCollectionAllocation: decodeFeature.MaxCollectionAllocation,
                maxDepth: decodeFeature.MaxDepth);

            var items = new List<T>();
            do
            {
                items.Add(decodeFunc(ref decoder));
            }
            while (decoder.Consumed < buffer.Length);

            return items;
        }

        async static Task FillWriterAsync(
            Protocol protocol,
            PipeReader payload,
            SliceEncoding encoding,
            ISliceFeature decodeFeature,
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
                    readResult.ThrowIfCanceled(protocol);
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
