// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Slice.Operations.Internal;
using System.Buffers;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;

namespace IceRpc.Slice.Operations;

/// <summary>Provides extension methods for <see cref="PipeReader" />.</summary>
public static class PipeReaderExtensions
{
    // 4 = varuint62 encoding of the size (1)
    // 252 = varint32 encoding of the tag end marker (-1)
    private static readonly ReadOnlySequence<byte> _emptyStructPayload = new([4, 252]);

    extension(PipeReader)
    {
        /// <summary>Creates a request or response payload holding an empty Slice struct.</summary>
        /// <returns>The payload.</returns>
        public static PipeReader CreateEmptySliceStructPayload() => PipeReader.Create(_emptyStructPayload);
    }

    /// <summary>Creates an async stream over a pipe reader to decode streamed elements.</summary>
    /// <typeparam name="T">The type of the element being decoded.</typeparam>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="elementSize">The size in bytes of one element.</param>
    /// <param name="sliceFeature">The Slice feature to customize the decoding.</param>
    /// <returns>The async stream to decode and return the streamed elements.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="elementSize" /> is equal of inferior to
    /// <c>0</c>.</exception>
    /// <remarks>The reader ownership is transferred to the returned async stream. The caller should no longer use
    /// the reader after this call, and must dispose the returned async stream when done to release the reader.
    /// </remarks>
    public static IAsyncStream<T> ToAsyncStream<T>(
        this PipeReader reader,
        DecodeFunc<T> decodeFunc,
        int elementSize,
        ISliceFeature? sliceFeature = null)
    {
        if (elementSize <= 0)
        {
            reader.Complete();
            throw new ArgumentException("The element size must be greater than 0.", nameof(elementSize));
        }

        sliceFeature ??= SliceFeature.Default;
        return new AsyncStream<T>(reader, ReadAsync, DecodeBuffer);

        IEnumerable<T> DecodeBuffer(ReadOnlySequence<byte> buffer)
        {
            // Since the elements are fixed-size, they can't contain service addresses hence baseProxy can remain null.
            var decoder = new SliceDecoder(
                buffer,
                maxCollectionAllocation: sliceFeature.MaxCollectionAllocation);

            var items = new T[buffer.Length / elementSize];
            for (int i = 0; i < items.Length; ++i)
            {
                items[i] = decodeFunc(ref decoder);
            }
            decoder.CheckEndOfBuffer();
            return items;
        }

        async ValueTask<ReadResult> ReadAsync(PipeReader reader, CancellationToken cancellationToken)
        {
            // Read the bytes for at least one element.
            // Note that the max number of bytes we can read in one shot is limited by the flow control of the
            // underlying transport.
            ReadResult readResult = await reader.ReadAtLeastAsync(elementSize, cancellationToken).ConfigureAwait(false);

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

    /// <summary>Creates an async stream over a pipe reader to decode variable size streamed elements.</summary>
    /// <typeparam name="T">The stream element type.</typeparam>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="sender">The proxy that sent the request, if applicable.</param>
    /// <param name="sliceFeature">The slice feature to customize the decoding.</param>
    /// <returns>The async stream to decode and return the streamed members.</returns>
    /// <remarks>The reader ownership is transferred to the returned async stream. The caller should no longer use
    /// the reader after this call, and must dispose the returned async stream when done to release the reader.
    /// </remarks>
    public static IAsyncStream<T> ToAsyncStream<T>(
        this PipeReader reader,
        DecodeFunc<T> decodeFunc,
        ISliceProxy? sender = null,
        ISliceFeature? sliceFeature = null)
    {
        sliceFeature ??= SliceFeature.Default;
        ISliceProxy? baseProxy = sliceFeature.BaseProxy ?? sender;
        return new AsyncStream<T>(reader, ReadAsync, DecodeBuffer);

        IEnumerable<T> DecodeBuffer(ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(buffer, baseProxy, sliceFeature.MaxCollectionAllocation);

            var items = new List<T>();
            do
            {
                items.Add(decodeFunc(ref decoder));
            }
            while (decoder.Consumed < buffer.Length);

            return items;
        }

        ValueTask<ReadResult> ReadAsync(PipeReader reader, CancellationToken cancellationToken) =>
            reader.ReadSliceSegmentAsync(sliceFeature.MaxSegmentSize, cancellationToken);
    }
}
