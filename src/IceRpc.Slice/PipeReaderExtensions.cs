// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using ZeroC.Slice;

namespace IceRpc.Slice;

/// <summary>Provides extension methods for <see cref="PipeReader" /> to decode streamed elements.</summary>
public static class PipeReaderExtensions
{
    /// <summary>Creates an async enumerable over a pipe reader to decode streamed elements.</summary>
    /// <typeparam name="T">The type of the element being decoded.</typeparam>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="elementSize">The size in bytes of one element.</param>
    /// <param name="sliceFeature">The Slice feature to customize the decoding.</param>
    /// <returns>The async enumerable to decode and return the streamed elements.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="elementSize" /> is equal of inferior to
    /// <c>0</c>.</exception>
    /// <remarks>The reader ownership is transferred to the returned async enumerable. The caller should no longer use
    /// the reader after this call.</remarks>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this PipeReader reader,
        SliceEncoding encoding,
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
        return reader.ToAsyncEnumerable(ReadAsync, DecodeBuffer);

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

    /// <summary>Creates an async enumerable over a pipe reader to decode variable size streamed elements.</summary>
    /// <typeparam name="T">The stream element type.</typeparam>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="decodeFunc">The function used to decode the streamed member.</param>
    /// <param name="sender">The proxy that sent the request, if applicable.</param>
    /// <param name="sliceFeature">The slice feature to customize the decoding.</param>
    /// <returns>The async enumerable to decode and return the streamed members.</returns>
    /// <remarks>The reader ownership is transferred to the returned async enumerable. The caller should no longer use
    /// the reader after this call.</remarks>
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this PipeReader reader,
        SliceEncoding encoding,
        DecodeFunc<T> decodeFunc,
        GenericProxy? sender = null,
        ISliceFeature? sliceFeature = null)
    {
        sliceFeature ??= SliceFeature.Default;

        Func<ServiceAddress, GenericProxy>? proxyFactory = sliceFeature.ProxyFactory;
        if (proxyFactory is null && sender is GenericProxy proxy)
        {
            proxyFactory = proxy.With;
        }
        return reader.ToAsyncEnumerable(ReadAsync, DecodeBuffer);

        IEnumerable<T> DecodeBuffer(ReadOnlySequence<byte> buffer)
        {
            // No activator or max depth since streams are Slice2+.
            var decoder = new SliceDecoder(buffer, encoding, proxyFactory, sliceFeature.MaxCollectionAllocation);

            var items = new List<T>();
            do
            {
                items.Add(decodeFunc(ref decoder));
            }
            while (decoder.Consumed < buffer.Length);

            return items;
        }

        ValueTask<ReadResult> ReadAsync(PipeReader reader, CancellationToken cancellationToken) =>
            reader.ReadSegmentAsync(encoding, sliceFeature.MaxSegmentSize, cancellationToken);
    }

    /// <summary>Decodes an async enumerable from a pipe reader.</summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="readFunc">The function used to read enough data to decode elements returned by the
    /// enumerable.</param>
    /// <param name="decodeBufferFunc">The function used to decode an element.</param>
    /// <param name="cancellationToken">The cancellation token which is provided to <see
    /// cref="IAsyncEnumerable{T}.GetAsyncEnumerator(CancellationToken)" />.</param>
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this PipeReader reader,
        Func<PipeReader, CancellationToken, ValueTask<ReadResult>> readFunc,
        Func<ReadOnlySequence<byte>, IEnumerable<T>> decodeBufferFunc,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        try
        {
            while (true)
            {
                ReadResult readResult;

                try
                {
                    readResult = await readFunc(reader, cancellationToken).ConfigureAwait(false);

                    if (readResult.IsCanceled)
                    {
                        // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
                        throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
                    }
                    if (readResult.Buffer.IsEmpty)
                    {
                        Debug.Assert(readResult.IsCompleted);
                        yield break;
                    }
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken)
                {
                    // Canceling the cancellation token is a normal way to complete an iteration.
                    yield break;
                }

                IEnumerable<T> elements = decodeBufferFunc(readResult.Buffer);
                reader.AdvanceTo(readResult.Buffer.End);

                foreach (T item in elements)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        yield break;
                    }
                    yield return item;
                }

                if (readResult.IsCompleted)
                {
                    yield break;
                }
            }
        }
        finally
        {
            reader.Complete();
        }
    }
}
