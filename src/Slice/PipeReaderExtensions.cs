// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.IO.Pipelines;

namespace Slice;

/// <summary>Extension methods to decode payloads carried by a PipeReader.</summary>
public static class PipeReaderExtensions
{
    /// <summary>Reads a Slice segment from a pipe reader.</summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding.</param>
    /// <param name="maxSize">The maximum size of this segment.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A read result with the segment read from the reader unless <see cref="ReadResult.IsCanceled" /> is
    /// <see langword="true" />.</returns>
    /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded or the segment size
    /// exceeds <paramref name="maxSize" />.</exception>
    /// <remarks>The caller must call AdvanceTo on the reader, as usual. With Slice1, this method reads all
    /// the remaining bytes in the reader; otherwise, this method reads the segment size in the segment and returns
    /// exactly segment size bytes. This method often examines the buffer it returns as part of ReadResult,
    /// therefore the caller should never examine less than Buffer.End.</remarks>
    public static async ValueTask<ReadResult> ReadSegmentAsync(
        this PipeReader reader,
        SliceEncoding encoding,
        int maxSize,
        CancellationToken cancellationToken)
    {
        Debug.Assert(maxSize is > 0 and < int.MaxValue);

        // This method does not attempt to read the reader synchronously. A caller that wants a sync attempt can
        // call TryReadSegment.

        if (encoding == SliceEncoding.Slice1)
        {
            // We read everything up to the maxSize + 1.
            // It's maxSize + 1 and not maxSize because if the segment's size is maxSize, we could get
            // readResult.IsCompleted == false even though the full segment was read.

            ReadResult readResult = await reader.ReadAtLeastAsync(maxSize + 1, cancellationToken).ConfigureAwait(false);

            if (readResult.IsCompleted && readResult.Buffer.Length <= maxSize)
            {
                return readResult;
            }
            else
            {
                reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                throw new InvalidDataException("The segment size exceeds the maximum value.");
            }
        }
        else
        {
            ReadResult readResult;
            int segmentSize;

            while (true)
            {
                readResult = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    if (IsCompleteSegment(ref readResult, maxSize, out segmentSize, out long consumed))
                    {
                        return readResult;
                    }
                    else if (segmentSize > 0)
                    {
                        Debug.Assert(consumed > 0);

                        // We decoded the segmentSize and examined the whole buffer but it was not sufficient.
                        reader.AdvanceTo(readResult.Buffer.GetPosition(consumed), readResult.Buffer.End);
                        break; // while
                    }
                    else
                    {
                        Debug.Assert(!readResult.IsCompleted); // see IsCompleteSegment
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        // and continue loop with at least one additional byte
                    }
                }
                catch
                {
                    // A ReadAsync or TryRead method that throws an exception should not leave the reader in a
                    // "reading" state.
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    throw;
                }
            }

            readResult = await reader.ReadAtLeastAsync(segmentSize, cancellationToken).ConfigureAwait(false);

            if (readResult.IsCanceled)
            {
                return readResult;
            }

            if (readResult.Buffer.Length < segmentSize)
            {
                Debug.Assert(readResult.IsCompleted);
                reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                throw new InvalidDataException(
                    $"The payload has {readResult.Buffer.Length} bytes, but {segmentSize} bytes were expected.");
            }

            return readResult.Buffer.Length == segmentSize ? readResult :
                new ReadResult(readResult.Buffer.Slice(0, segmentSize), isCanceled: false, isCompleted: false);
        }
    }

    /// <summary>Attempts to read a Slice segment from a pipe reader.</summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding.</param>
    /// <param name="maxSize">The maximum size of this segment.</param>
    /// <param name="readResult">The read result.</param>
    /// <returns><see langword="true" /> when <paramref name="readResult" /> contains the segment read synchronously, or
    /// the call was cancelled; otherwise, <see langword="false" />.</returns>
    /// <exception cref="InvalidDataException">Thrown when the segment size could not be decoded or the segment size
    /// exceeds the max segment size.</exception>
    /// <remarks>When this method returns <see langword="true" />, the caller must call AdvanceTo on the reader, as
    /// usual. This method often examines the buffer it returns as part of ReadResult, therefore the caller should never
    /// examine less than Buffer.End when the return value is <see langword="true" />. When this method returns
    /// <see langword="false" />, the caller must call <see cref="ReadSegmentAsync" />.</remarks>
    public static bool TryReadSegment(
        this PipeReader reader,
        SliceEncoding encoding,
        int maxSize,
        out ReadResult readResult)
    {
        Debug.Assert(maxSize is > 0 and < int.MaxValue);

        if (encoding == SliceEncoding.Slice1)
        {
            if (reader.TryRead(out readResult))
            {
                if (readResult.IsCanceled)
                {
                    return true; // and the buffer does not matter
                }

                if (readResult.Buffer.Length > maxSize)
                {
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    throw new InvalidDataException("The segment size exceeds the maximum value.");
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
                try
                {
                    if (IsCompleteSegment(ref readResult, maxSize, out int segmentSize, out long _))
                    {
                        return true;
                    }
                    else
                    {
                        // we don't consume anything but examined the whole buffer since it's not sufficient.
                        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                        readResult = default;
                        return false;
                    }
                }
                catch
                {
                    reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                    throw;
                }
            }
            else
            {
                return false;
            }
        }
    }

    /// <summary>Checks if a read result holds a complete Slice segment and if the segment size does not exceed the
    /// maximum size.</summary>
    /// <returns><see langword="true" /> when <paramref name="readResult" /> holds a complete segment or is canceled;
    /// otherwise, <see langword="false" />.</returns>
    /// <remarks><paramref name="segmentSize" /> and <paramref name="consumed" /> can be set when this method returns
    /// <see langword="false" />. In this case, both segmentSize and consumed are greater than 0.</remarks>
    private static bool IsCompleteSegment(
        ref ReadResult readResult,
        int maxSize,
        out int segmentSize,
        out long consumed)
    {
        consumed = 0;
        segmentSize = -1;

        if (readResult.IsCanceled)
        {
            return true; // and buffer etc. does not matter
        }

        if (readResult.Buffer.IsEmpty)
        {
            Debug.Assert(readResult.IsCompleted);
            segmentSize = 0;
            return true; // the caller will call AdvanceTo on this buffer.
        }

        var decoder = new SliceDecoder(readResult.Buffer, SliceEncoding.Slice2);
        if (decoder.TryDecodeVarUInt62(out ulong ulongSize))
        {
            consumed = decoder.Consumed;

            try
            {
                segmentSize = checked((int)ulongSize);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("The segment size can't be larger than int.MaxValue.", exception);
            }

            if (segmentSize > maxSize)
            {
                throw new InvalidDataException("The segment size exceeds the maximum value.");
            }

            if (readResult.Buffer.Length >= consumed + segmentSize)
            {
                // When segmentSize is 0, we return a read result with an empty buffer.
                readResult = new ReadResult(
                    readResult.Buffer.Slice(readResult.Buffer.GetPosition(consumed), segmentSize),
                    isCanceled: false,
                    isCompleted: readResult.IsCompleted &&
                        readResult.Buffer.Length == consumed + segmentSize);

                return true;
            }

            if (readResult.IsCompleted && consumed + segmentSize > readResult.Buffer.Length)
            {
                throw new InvalidDataException(
                    $"The payload has {readResult.Buffer.Length} bytes, but {segmentSize} bytes were expected.");
            }

            // segmentSize and consumed are set and can be used by the caller.
            return false;
        }
        else if (readResult.IsCompleted)
        {
            throw new InvalidDataException("Received a Slice segment with fewer bytes than promised.");
        }
        else
        {
            segmentSize = -1;
            return false;
        }
    }
}
