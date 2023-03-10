// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice;

/// <summary>Extension methods for <see cref="IncomingFrame" />.</summary>
public static class IncomingFrameExtensions
{
    /// <summary>Detaches the payload from the incoming frame. The caller takes ownership of the returned payload
    /// pipe reader, and <see cref="IncomingFrame.Payload" /> becomes invalid.</summary>
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
            throw new ArgumentException("The element size must be greater than 0.", nameof(elementSize));
        }

        sliceFeature ??= SliceFeature.Default;
        return payload.ToAsyncEnumerable(ReadAsync, DecodeBuffer);

        IEnumerable<T> DecodeBuffer(ReadOnlySequence<byte> buffer)
        {
            // Since the elements are fixed-size, they can't contain proxies hence serviceProxyFactory can remain null.
            var decoder = new SliceDecoder(
                buffer,
                encoding,
                maxCollectionAllocation: sliceFeature.MaxCollectionAllocation,
                maxDepth: sliceFeature.MaxDepth);

            var items = new T[buffer.Length / elementSize];
            for (int i = 0; i < items.Length; ++i)
            {
                items[i] = decodeFunc(ref decoder);
            }
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return items;
        }

        async ValueTask<ReadResult> ReadAsync(PipeReader payload, CancellationToken cancellationToken)
        {
            // Read the bytes for at least one element.
            // Note that the max number of bytes we can read in one shot is limited by the flow control of the
            // underlying transport.
            ReadResult readResult = await payload.ReadAtLeastAsync(elementSize, cancellationToken)
                .ConfigureAwait(false);

            // Check if the buffer contains extra bytes that we need to remove.
            ReadOnlySequence<byte> buffer = readResult.Buffer;
            if (elementSize > 1 && buffer.Length > elementSize)
            {
                long extra = buffer.Length % elementSize;
                if (extra > 0)
                {
                    buffer = buffer.Slice(0, buffer.Length - extra);
                    return new ReadResult(buffer, isCanceled: readResult.IsCanceled, isCompleted: false);
                }
            }

            // Return the read result as-is.
            return readResult;
        }
    }

    /// <summary>Creates an async enumerable over the payload reader of an incoming request to decode variable size
    /// streamed elements.</summary>
    /// <typeparam name="T">The stream element type.</typeparam>
    /// <param name="payload">The incoming request.</param>
    /// <param name="encoding">The encoding of the request payload.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="templateProxy">The template proxy.</param>
    /// <param name="sliceFeature">The slice feature to customize the decoding.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this PipeReader payload,
        SliceEncoding encoding,
        DecodeFunc<T> decodeFunc,
        GenericProxy? templateProxy = null,
        ISliceFeature? sliceFeature = null)
    {
        sliceFeature ??= SliceFeature.Default;
        return payload.ToAsyncEnumerable(ReadAsync, DecodeBuffer);

        IEnumerable<T> DecodeBuffer(ReadOnlySequence<byte> buffer)
        {
            // No activator or max depth since streams are Slice2+.
            var decoder = new SliceDecoder(
                buffer,
                encoding,
                sliceFeature.ProxyFactory,
                templateProxy,
                sliceFeature.MaxCollectionAllocation);

            var items = new List<T>();
            do
            {
                items.Add(decodeFunc(ref decoder));
            }
            while (decoder.Consumed < buffer.Length);

            return items;
        }

        ValueTask<ReadResult> ReadAsync(PipeReader payload, CancellationToken cancellationToken) =>
            payload.ReadSegmentAsync(encoding, sliceFeature.MaxSegmentSize, cancellationToken);
    }
}
