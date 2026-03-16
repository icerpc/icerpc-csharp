// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice.Codec;
using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc.Ice.Operations.Internal;

/// <summary>Provides extension methods for <see cref="IncomingFrame" /> to decode its payload when this payload is
/// encoded with the Ice encoding.</summary>
internal static class IncomingFrameExtensions
{
    /// <summary>Decodes arguments or a response value from a pipe reader.</summary>
    /// <param name="frame">The incoming frame.</param>
    /// <param name="feature">The Ice feature.</param>
    /// <param name="baseProxy">The base proxy.</param>
    /// <param name="decodeFunc">The decode function for the payload arguments or return value.</param>
    /// <param name="activator">The activator.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The decode value.</returns>
    internal static ValueTask<T> DecodeValueAsync<T>(
        this IncomingFrame frame,
        IIceFeature feature,
        IIceProxy? baseProxy,
        DecodeFunc<T> decodeFunc,
        IActivator activator,
        CancellationToken cancellationToken)
    {
        return frame.Payload.TryReadIceSegment(feature.MaxSegmentSize, out ReadResult readResult) ?
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

            var decoder = new IceDecoder(
                readResult.Buffer,
                IceEncoding.Ice1,
                baseProxy,
                feature.MaxCollectionAllocation,
                activator,
                feature.MaxDepth);
            T value = decodeFunc(ref decoder);

            // useTagEndMarker is false because we're decoding parameters or a return value, not class/exception fields.
            decoder.SkipTagged(useTagEndMarker: false);
            decoder.CheckEndOfBuffer();

            frame.Payload.AdvanceTo(readResult.Buffer.End);
            return value;
        }

        async ValueTask<T> PerformDecodeAsync() =>
            DecodeSegment(await frame.Payload.ReadIceSegmentAsync(
                feature.MaxSegmentSize,
                cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Reads/decodes empty args or a void return value.</summary>
    /// <param name="frame">The incoming frame.</param>
    /// <param name="feature">The Ice feature.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    internal static ValueTask DecodeVoidAsync(
        this IncomingFrame frame,
        IIceFeature feature,
        CancellationToken cancellationToken)
    {
        if (frame.Payload.TryReadIceSegment(feature.MaxSegmentSize, out ReadResult readResult))
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
                var decoder = new IceDecoder(readResult.Buffer, IceEncoding.Ice1);
                decoder.SkipTagged(useTagEndMarker: false); // false because we're decoding parameters, not class/exception fields
                decoder.CheckEndOfBuffer();
            }
            frame.Payload.AdvanceTo(readResult.Buffer.End);
        }

        async ValueTask PerformDecodeAsync() =>
            DecodeSegment(await frame.Payload.ReadIceSegmentAsync(
                feature.MaxSegmentSize,
                cancellationToken).ConfigureAwait(false));
    }
}
