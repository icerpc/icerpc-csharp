// Copyright (c) ZeroC, Inc.

using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Ice.Operations.Internal;

/// <summary>Provides extension methods for <see cref="PipeReader" /> to read payloads.</summary>
internal static class PipeReaderExtensions
{
    /// <summary>Reads the full payload from a pipe reader.</summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="maxSize">The maximum size of this payload.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A read result with the payload read from the reader unless <see cref="ReadResult.IsCanceled" /> is
    /// <see langword="true" />.</returns>
    /// <exception cref="InvalidDataException">Thrown when the payload size exceeds <paramref name="maxSize" />.
    /// </exception>
    /// <remarks>The caller must call AdvanceTo on the reader, as usual. This method reads all the remaining bytes in
    /// the reader.</remarks>
    internal static async ValueTask<ReadResult> ReadFullPayloadAsync(
        this PipeReader reader,
        int maxSize,
        CancellationToken cancellationToken)
    {
        Debug.Assert(maxSize is > 0 and < int.MaxValue);

        // This method does not attempt to read the reader synchronously. A caller that wants a sync attempt can
        // call TryReadFullPayload.

        // We read everything up to the maxSize + 1.
        // It's maxSize + 1 and not maxSize because if the payload's size is maxSize, we could get
        // readResult.IsCompleted == false even though the full payload was read.
        ReadResult readResult = await reader.ReadAtLeastAsync(maxSize + 1, cancellationToken).ConfigureAwait(false);

        if (readResult.IsCompleted && readResult.Buffer.Length <= maxSize)
        {
            return readResult;
        }
        else
        {
            reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            throw new InvalidDataException("The payload size exceeds the maximum value.");
        }
    }

    /// <summary>Attempts to read the full payload from a pipe reader.</summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="maxSize">The maximum size of this payload.</param>
    /// <param name="readResult">The read result.</param>
    /// <returns><see langword="true" /> when <paramref name="readResult" /> contains the payload read synchronously, or
    /// the call was cancelled; otherwise, <see langword="false" />.</returns>
    /// <exception cref="InvalidDataException">Thrown when the payload size exceeds the max payload size.</exception>
    /// <remarks>When this method returns <see langword="true" />, the caller must call AdvanceTo on the reader, as
    /// usual. When this method returns <see langword="false" />, the caller must call
    /// <see cref="ReadFullPayloadAsync" />.</remarks>
    internal static bool TryReadFullPayload(
        this PipeReader reader,
        int maxSize,
        out ReadResult readResult)
    {
        Debug.Assert(maxSize is > 0 and < int.MaxValue);

        if (reader.TryRead(out readResult))
        {
            if (readResult.IsCanceled)
            {
                return true; // and the buffer does not matter
            }

            if (readResult.Buffer.Length > maxSize)
            {
                reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                throw new InvalidDataException("The payload size exceeds the maximum value.");
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
}
