// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
    internal static class PipeReaderExtensions
    {
        private const int MaxSegmentSize = 4 * 1024 * 1024; // TODO: make MaxSegmentSize configurable

        /// <summary>Reads a Slice segment from a pipe reader.</summary>
        /// <param name="reader">The pipe reader.</param>
        /// <param name="encoding">The encoding.</param>
        /// <param name="cancel">The cancellation token.</param>
        /// <returns>A read result with the segment read from the reader unless <see cref="ReadResult.IsCanceled"/> is
        /// <c>true</c>.</returns>
        /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded.</exception>
        /// <remarks>The caller must call AdvanceTo on the reader, as usual. With encoding 1.1, this method reads all
        /// the remaining bytes in the reader; otherwise, this method reads the segment size in the segment and returns
        /// exactly segment size bytes.</remarks>
        internal static async ValueTask<ReadResult> ReadSegmentAsync(
            this PipeReader reader,
            SliceEncoding encoding,
            CancellationToken cancel)
        {
            if (encoding == Encoding.Slice11)
            {
                // We read everything up to the MaxSegmentSize + 1.
                // It's MaxSegmentSize + 1 and not MaxSegmentSize because if the segment's size is MaxSegmentSize,
                // we could get readResult.IsCompleted == false even though the full segment was read.

                ReadResult readResult = await reader.ReadAtLeastAsync(MaxSegmentSize + 1, cancel).ConfigureAwait(false);

                return readResult.IsCompleted && readResult.Buffer.Length <= MaxSegmentSize ? readResult :
                    throw new InvalidDataException("segment size exceeds maximum value");
            }
            else
            {
                ReadResult readResult;
                int segmentSize;

                while (true)
                {
                    readResult = await reader.ReadAsync(cancel).ConfigureAwait(false);

                    if (readResult.IsCanceled)
                    {
                        return readResult;
                    }
                    if (readResult.Buffer.IsEmpty)
                    {
                        Debug.Assert(readResult.IsCompleted);
                        return readResult; // size == 0, the caller will AdvanceTo on this buffer.
                    }

                    if (TryDecodeSize(readResult.Buffer, out segmentSize, out long consumed))
                    {
                        if (segmentSize > MaxSegmentSize)
                        {
                            throw new InvalidDataException($"segment size '{segmentSize}' exceeds maximum value");
                        }

                        // When segmentSize is 0, the code below returns an empty buffer.
                        if (readResult.Buffer.Length >= segmentSize + consumed)
                        {
                            return new ReadResult(
                                readResult.Buffer.Slice(readResult.Buffer.GetPosition(consumed), segmentSize),
                                isCanceled: false,
                                isCompleted: readResult.IsCompleted &&
                                    readResult.Buffer.Length == consumed + segmentSize);
                        }

                        if (readResult.IsCompleted && consumed + segmentSize > readResult.Buffer.Length)
                        {
                            throw new InvalidDataException(
                                $"payload stream has fewer than '{segmentSize}' bytes");
                        }

                        // We examined the whole buffer and it was not sufficient.
                        reader.AdvanceTo(readResult.Buffer.GetPosition(consumed), readResult.Buffer.End);
                        break;
                    }
                    else if (readResult.IsCompleted)
                    {
                        throw new InvalidDataException("received invalid segment size");
                    }
                    else
                    {
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        // and continue loop
                    }
                }

                readResult = await reader.ReadAtLeastAsync(segmentSize, cancel).ConfigureAwait(false);

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

        internal static bool TryReadSegment(this PipeReader reader, SliceEncoding encoding, out ReadResult readResult)
        {
            if (encoding == Encoding.Slice11)
            {
                if (reader.TryRead(out readResult))
                {
                    if (readResult.IsCanceled)
                    {
                        return true; // and the buffer does not matter
                    }

                    if (readResult.Buffer.Length > MaxSegmentSize)
                    {
                        throw new InvalidDataException(
                            $"segment size '{readResult.Buffer.Length}' exceeds maximum value");
                    }

                    if (readResult.IsCompleted)
                    {
                        return true;
                    }
                    else
                    {
                        // don't consume anything but mark the whole buffer as examined - we need more.
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    }
                }

                readResult = default;
                return false;
            }
            else
            {
                if (reader.TryRead(out readResult))
                {
                    if (readResult.IsCanceled)
                    {
                        return true; // and buffer does not matter
                    }

                    if (readResult.Buffer.IsEmpty)
                    {
                        Debug.Assert(readResult.IsCompleted);
                        return true; // size == 0, the caller will AdvanceTo on this buffer.
                    }

                    if (TryDecodeSize(readResult.Buffer, out int segmentSize, out long consumed))
                    {
                        if (segmentSize > MaxSegmentSize)
                        {
                            throw new InvalidDataException($"segment size '{segmentSize}' exceeds maximum value");
                        }

                        if (readResult.IsCompleted && consumed + segmentSize > readResult.Buffer.Length)
                        {
                            throw new InvalidDataException(
                                $"payload stream has fewer than '{segmentSize}' bytes");
                        }

                        // When segmentSize is 0, the code below returns an empty buffer.
                        if (readResult.Buffer.Length >= segmentSize + consumed)
                        {
                            readResult = new ReadResult(
                                readResult.Buffer.Slice(readResult.Buffer.GetPosition(consumed), segmentSize),
                                isCanceled: false,
                                isCompleted: readResult.IsCompleted &&
                                    readResult.Buffer.Length == consumed + segmentSize);

                            return true;
                        }
                        // else fall back as if TryDecodeSize returned false
                    }

                    // we don't consume anything but examined the whole buffer since it's not sufficient.
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    readResult = default;
                    return false;
                }
                else
                {
                    return false;
                }
            }
        }

        private static bool TryDecodeSize(ReadOnlySequence<byte> buffer, out int size, out long consumed)
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
}
