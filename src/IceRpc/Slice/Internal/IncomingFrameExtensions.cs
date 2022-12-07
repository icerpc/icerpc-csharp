// Copyright (c) ZeroC, Inc. All rights reserved.

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
    /// <param name="templateProxy">The template proxy.</param>
    /// <param name="decodeFunc">The decode function for the payload arguments or return value.</param>
    /// <param name="activator">The activator.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decode value.</returns>
    internal static ValueTask<T> DecodeValueAsync<T>(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature feature,
        ServiceProxy? templateProxy,
        DecodeFunc<T> decodeFunc,
        IActivator? activator,
        CancellationToken cancellationToken)
    {
        return frame.Payload.TryReadSegment(encoding, feature.MaxSegmentSize, out ReadResult readResult) ?
            new(DecodeSegment(readResult)) :
            PerformDecodeAsync();

        // All the logic is in this local function.
        T DecodeSegment(ReadResult readResult)
        {
            // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
            if (readResult.IsCanceled)
            {
                throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
            }

            var decoder = new SliceDecoder(
                readResult.Buffer,
                encoding,
                feature.ServiceProxyFactory,
                templateProxy,
                feature.MaxCollectionAllocation,
                activator,
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
                cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Reads/decodes empty args or a void return value.</summary>
    /// <param name="frame">The incoming frame.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="feature">The Slice feature.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    internal static ValueTask DecodeVoidAsync(
        this IncomingFrame frame,
        SliceEncoding encoding,
        ISliceFeature feature,
        CancellationToken cancellationToken)
    {
        if (frame.Payload.TryReadSegment(encoding, feature.MaxSegmentSize, out ReadResult readResult))
        {
            DecodeSegment(readResult);
            return default;
        }

        return PerformDecodeAsync();

        // All the logic is in this local function.
        void DecodeSegment(ReadResult readResult)
        {
            // We never call CancelPendingRead; an interceptor or middleware can but it's not correct.
            if (readResult.IsCanceled)
            {
                throw new InvalidOperationException("Unexpected call to CancelPendingRead.");
            }

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
                cancellationToken).ConfigureAwait(false));
    }
}
