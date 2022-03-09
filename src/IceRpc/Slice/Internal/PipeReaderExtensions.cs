// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
    internal static class PipeReaderExtensions
    {
        /// <summary>Decodes the size of a segment from a PipeReader.</summary>
        /// <remarks>The caller does not (and cannot) call AdvanceTo after calling this method.</remarks>
        internal static async ValueTask<(int Size, bool IsCanceled, bool IsCompleted)> DecodeSegmentSizeAsync(
            this PipeReader reader,
            CancellationToken cancel)
        {
            while (true)
            {
                ReadResult readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

                if (readResult.IsCanceled)
                {
                    return (-1, true, false);
                }

                if (readResult.Buffer.IsEmpty)
                {
                    Debug.Assert(readResult.IsCompleted);
                    reader.AdvanceTo(readResult.Buffer.End);
                    return (0, false, true);
                }

                if (TryDecodeSize(readResult.Buffer, out int size, out long consumed))
                {
                    reader.AdvanceTo(readResult.Buffer.GetPosition(consumed));
                    return (size, false, readResult.IsCompleted && readResult.Buffer.Length == consumed);
                }
                else
                {
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                }
            }

            static bool TryDecodeSize(ReadOnlySequence<byte> buffer, out int size, out long consumed)
            {
                var decoder = new SliceDecoder(buffer, Encoding.Slice20);
                if (decoder.TryDecodeSize(out size))
                {
                    consumed = decoder.Consumed;
                    return true;
                }
                else
                {
                    consumed = 0;
                    return false;
                }
            }
        }

        /// <summary>Reads a segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A read result with the segment read from the reader unless IsCanceled is true.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded.</exception>
        /// <remarks>The caller must call AdvanceTo when the returned segment length is greater than 0. This method
        /// never marks the reader as completed.</remarks>
        internal static async ValueTask<ReadResult> ReadSegmentAsync(this PipeReader reader, CancellationToken cancel)
        {
            (int segmentSize, bool isCanceled, bool isCompleted) =
                await reader.DecodeSegmentSizeAsync(cancel).ConfigureAwait(false);

            if (isCanceled || segmentSize == 0)
            {
                return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled, isCompleted);
            }

            if (isCompleted)
            {
                throw new InvalidDataException($"no byte in segment with {segmentSize} bytes");
            }

            ReadResult readResult = await reader.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                return readResult;
            }

            if (readResult.Buffer.Length < segmentSize)
            {
                throw new InvalidDataException($"too few bytes in segment with {segmentSize} bytes");
            }

            return readResult.Buffer.Length == segmentSize ? readResult :
                new ReadResult(readResult.Buffer.Slice(0, segmentSize), isCanceled: false, isCompleted: false);
        }
    }
}
